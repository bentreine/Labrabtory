using System.Globalization;
using System.Text.Json;
using System.Threading;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using KellerPostman.Salesforce.Sdk;
using KellerPostman.Salesforce.Sdk.Interfaces;
using KellerPostman.Salesforce.Sdk.Models;
using KellerPostman.Salesforce.Sdk.V2;
using Laboratory;
using Laboratory.ArcherClient;
using Laboratory.Models;
using Laboratory.SalesforceClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Laboratory.RagicReview;
using Task = System.Threading.Tasks.Task;

public class ReuploadBoxFiles
{
    private readonly IBoxClient boxClient;

    private readonly IDocumentStoreClient documentStoreClient;

    private readonly ILogger<ReuploadBoxFiles> _logger;

    private readonly ISalesforceDirectV2Sdk _salesforceDirectV2Sdk;

    private readonly ISalesforceSdk _salesforceSdk;

    private readonly IArcherClient _archerClient;

    private readonly ILocalSalesforceClient _salesforceClient;

    private readonly string DBConnectionString;

    public ReuploadBoxFiles(IBoxClient boxClient, IDocumentStoreClient documentStoreClient, ISalesforceDirectV2Sdk salesforceDirectV2Sdk, ISalesforceSdk salesforceSdk, IArcherClient archerClient, ILocalSalesforceClient salesforceClient, ILogger<ReuploadBoxFiles> logger, IConfiguration configuration)
    {
        this.boxClient = boxClient;
        this.documentStoreClient = documentStoreClient;
        _logger = logger;
        _salesforceDirectV2Sdk = salesforceDirectV2Sdk;
        _archerClient = archerClient;
        _salesforceSdk = salesforceSdk;
        _salesforceClient = salesforceClient;
        DBConnectionString = configuration.GetSection("AppSettings")["PostgresConnectionString"];
    }
    public async System.Threading.Tasks.Task ReuploadFiles()
    {

        var filesToUpload = ParseCSVForFilesToUpload();
        _logger.LogInformation("Reuploading {0} files", filesToUpload.Count);
        double totalCount = filesToUpload.Count;
        double currentCount = 0;

        var tempDocumentList = new List<(string FilePath, string fileId)>(); // List of temp files to delete
        foreach (var fileToUpload in filesToUpload)
        {
            _logger.LogInformation("Reuploading files for {0}, matterId: {1}", fileToUpload.InjuredPartyName, fileToUpload.MatterId);

            double percentComplete = (currentCount / totalCount) * 100;
            _logger.LogInformation("{1}% complete", percentComplete);

            var ids = fileToUpload.GetMedicalRecordIds();
            var documents = await documentStoreClient.GetDocuments(ids);
            await boxClient.UploadMedicalRecordsToBox(fileToUpload.CaseName, fileToUpload.MatterId, fileToUpload.InjuredPartyName, documents, false);

            tempDocumentList.AddRange(documents);
            currentCount++;
        }
        _logger.LogInformation("Cleaning up files");


        double totalFiles = tempDocumentList.Count;
        double currentFile = 0;
        foreach (var (filePath, fileId) in tempDocumentList)
        {
            double percentComplete = (currentFile / totalFiles) * 100;
            _logger.LogInformation("{1}% complete", percentComplete);
            CleanUpLocalFile(filePath, fileId);
        }
        _logger.LogInformation("Reupload Complete :D");
    }

    public async Task ManualStartReview(string matterId)
    {
        _logger.LogInformation("Starting manual review for matterId: {MatterId}", matterId);
        var getMedicalReviewQuery = string.Format(SOQLQuries.GET_MEDICAL_REVIEW_BY_MATTER_ID, matterId);

        _logger.LogInformation("Querying Salesforce for medical review");

        try
        {
            var test = await _salesforceDirectV2Sdk.QueryAsync<Medical_Review__c>(getMedicalReviewQuery, default);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query Salesforce for medical review with query: {Query}", getMedicalReviewQuery);
            throw;
        }
        var salesforceMedicalReviewResponse = await _salesforceDirectV2Sdk.QueryAsync<Medical_Review__c>(getMedicalReviewQuery, default);

        var medicalReview = salesforceMedicalReviewResponse.Records.FirstOrDefault();
        var getDocumentsQuery = string.Format(SOQLQuries.GetFilesToReupload, matterId);

        _logger.LogInformation("Querying Salesforce for medical records");
        var medicalRecords = await _salesforceDirectV2Sdk.QueryAsync<DocumentInfo>(getDocumentsQuery, default);

        var medicalRecordIds = medicalRecords?.Records?.Select(x => x.Id).ToList();
        string medicalReviewId = medicalReview.Id;

        if (medicalRecordIds == null || !medicalRecordIds.Any())
        {
            _logger.LogWarning("No medical records found for matterId: {MatterId}", matterId);
            return;
        }
        _logger.LogInformation("Found {Count} medical records for matterId: {MatterId}", medicalRecordIds.Count, matterId);
        _logger.LogInformation("Found medical review for matterId: {MatterId}, medicalReviewId: {MedicalReviewId}", matterId, medicalReviewId);
        _logger.LogInformation("Injured Party Id: {InjuredPartyId}, Client Id: {ClientId}", medicalReview.Injured_Party__c, medicalReview.Client__c);
        Party? injuredParty = await _salesforceSdk.GetParty(medicalReview.Injured_Party__c);
        Party? client = medicalReview.Injured_Party__c == medicalReview.Client__c ? injuredParty : await _salesforceSdk.GetParty(medicalReview.Client__c);


        var recordReview = new RecordReview(
            medicalReview.Case_Type_Picklist__c,//This might cause me issues
            medicalReview.Matter__c ?? medicalReview.Legal_Case__c,
            injuredParty.Id,
            $"{injuredParty.FirstName} {injuredParty.LastName}",
            medicalReviewId);

        var archerId = await GetArcherId(medicalReview, injuredParty, client, medicalRecordIds);

        recordReview.MarkAccepted(archerId);



        recordReview.AppendMedicalRecordIds(medicalRecordIds);

        var insertStatement = recordReview.ToInsertStatement();
        _logger.LogInformation(insertStatement);

        var filePath = "InsertStatement.sql"; // You can change the file name/path as needed
        await File.WriteAllTextAsync(filePath, insertStatement);
        _logger.LogInformation("Insert statement written to {FilePath}", filePath);

        foreach (var medicalRecordId in medicalRecordIds)
        {
            await _salesforceClient.UpdateDocumentFileInfo(medicalRecordId, FileInfoStatus.InReview);
        }

        await UpdatePostgres(recordReview);
        _logger.LogInformation("Record review updated in Postgres for matterId: {MatterId}", matterId);

    }

    private async Task UpdatePostgres(RecordReview recordReview)
    {
        var connectionString = DBConnectionString;
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var query = recordReview.ToInsertStatement();
        using var command = new NpgsqlCommand(query, connection);
        await command.ExecuteNonQueryAsync();
        await connection.CloseAsync();
    }

    private async Task<int> GetArcherId(
         Medical_Review__c medicalReview,
         Party injuredParty,
         Party client,
         List<string> medicalRecordIds)
    {

        _logger.LogInformation("Uploading files to Box for matterId: {MatterId}", medicalReview.Matter__c ?? medicalReview.Legal_Case__c);
        await UploadFilesToBox(injuredParty, medicalReview, medicalRecordIds, default);

        var archerId = await _archerClient.CreateNewReview(new CreateNewArcherReviewRequest(
            medicalReview.Case_Type_Picklist__c,
            medicalReview.Matter__c ?? medicalReview.Legal_Case__c,
            medicalReview.Name,
            client.Id,
            client.FirstName,
            client.LastName,
            client.Phone,
            client.Email,
            client.DateOfBirth?.ToDateTime(TimeOnly.MinValue),
            injuredParty.FirstName,
            injuredParty.LastName,
            injuredParty.DateOfBirth?.ToDateTime(TimeOnly.MinValue),
            injuredParty.DateOfDeath,
            CalculatePartyAge(injuredParty)));

        return archerId;
    }

    private async Task UploadFilesToBox(Party injuredParty,
        Medical_Review__c medicalReview,
        List<string> medicalRecordIds,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Uploading files to Box for matterId: {MatterId}", medicalReview.Matter__c ?? medicalReview.Legal_Case__c);
        _logger.LogInformation("Getting Documents");
        var filePaths = await documentStoreClient.GetDocuments(medicalRecordIds, cancellationToken) ?? new List<(string TempFilePath, string SalesforceDocumentId)>();
        var injuredPartyName = $"{injuredParty.FirstName} {injuredParty.LastName}";
        _logger.LogInformation("Uploading {Count} files to Box for injured party: {InjuredPartyName}", filePaths.Count, injuredPartyName);
        await boxClient.UploadMedicalRecordsToBox(medicalReview.Case_Type_Picklist__c!, medicalReview.Matter__c!, injuredPartyName, filePaths);
    }



    private List<FilesToUpload> ParseCSVForFilesToUpload()
    {
        // Configure the CSV reader
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Don't throw an exception if a CSV line has fewer fields than the header record
            MissingFieldFound = null
        };
        using (var reader = new StreamReader("BoxReupload.csv"))
        using (var csv = new CsvReader(reader, config))
        {
            // Get the records and map them to your custom object
            var records = csv.GetRecords<FilesToUpload>();
            return records.ToList();
        }
    }

    private void CleanUpLocalFile(string? filePath, string? fileId)
    {
        if (filePath == null)
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file {FileId}", fileId);
        }
    }

    private string? CalculatePartyAge(Party injuredParty)
    {
        if (injuredParty.DateOfBirth.HasValue)
        {
            return injuredParty.DateOfDeath.HasValue
                ? (injuredParty.DateOfDeath.Value.Year - injuredParty.DateOfBirth.Value.Year).ToString()
                : (DateTime.Now.Year - injuredParty.DateOfBirth.Value.Year).ToString();
        }

        return null;
    }
}


public static class SOQLQuries
{
    public const string GetFilesToReupload = """
            SELECT
            Id,
            CreatedDate,
            litify_docs__Document_Category__c,
            litify_docs__Folder_Path__c,
            Status__c
        FROM litify_docs__File_Info__c WHERE litify_docs__Document_Category__c = 'Medical Records'
        AND Status__c = 'Not Reviewed' AND Matter__c = '{0}'
        """;

    public const string GET_MEDICAL_REVIEW_BY_MATTER_ID = """
        SELECT 
        Access_to_Resubmit__c,Attention_Notes__c,Bulk_Send__c,Case_Type_Picklist__c,
        Client__c,CreatedById,CreatedDate,Firm_Response__c,Id,
        Injured_Party__c,IsDeleted,KPLawID__c,LastActivityDate,
        LastModifiedById,LastModifiedDate,LastReferencedDate,
        LastViewedDate,Legacy_Litify_ID__c,Legal_Case__c,
        Matter__c,Medical_Review_JSON__c,Name,Needs_Firm_Attention__c,
        OwnerId,Power_of_One__c,Review_Submission_Date__c,
        Status_Date__c,Status__c,SystemModstamp
        FROM Medical_Review__c WHERE Matter__c = '{0}' OR Legal_Case__c = '{0}'
        """;
}

public class FilesToUpload
{
    public string MatterId {get; set;}
    public string InjuredPartyName {get; set;}
    public string CaseName {get; set;}
    public string MedicalRecordIds {get; set;}

        public virtual List<string> GetMedicalRecordIds()
    {
        if (string.IsNullOrEmpty(MedicalRecordIds))
        {
            return new List<string>();
        }
        return JsonSerializer.Deserialize<List<string>>(MedicalRecordIds) ?? new List<string>();
    }
}

public enum FileInfoStatus
{
    InReview,
    ReviewComplete
}


public class RecordReview
{
    public Guid Id { get; internal set; }
    public string CaseName { get; internal set; }
    public string MatterId { get; internal set; }
    public string InjuredPartyId { get; internal set; }
    public string InjuredPartyName { get; internal set; }
    public string? ReviewDetails { get; internal set; }
    public RecordReviewStatus StatusId { get; internal set; }
    public string? Error { get; internal set; }
    public string SalesforceId { get; internal set; }
    public int? ArcherId { get; internal set; }
    public Guid? DocumentReviewId { get; internal set; }
    public string? MedicalRecordIds { get; internal set; }

    public DateTime CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }

    public RecordReview(
        string caseName,
        string matterId,
        string injuredPartyId,
        string injuredPartyName,
        string salesforceId)
    {
        Id = Guid.NewGuid();
        CaseName = caseName;
        MatterId = matterId;
        InjuredPartyId = injuredPartyId;
        InjuredPartyName = injuredPartyName;
        StatusId = RecordReviewStatus.Unknown;
        SalesforceId = salesforceId;
        CreatedDate = DateTime.UtcNow;
        UpdatedDate = DateTime.UtcNow;
    }

    public string ToInsertStatement()
    {
        return $"""
            INSERT INTO RecordReviews (Id, CaseName, MatterId, InjuredPartyId, InjuredPartyName, StatusId, SalesforceId, CreatedDate, UpdatedDate, ArcherId, MedicalRecordIds)
            VALUES ('{Id}', '{CaseName}', '{MatterId}', '{InjuredPartyId}', '{InjuredPartyName}', {StatusId}, '{SalesforceId}', '{CreatedDate:yyyy-MM-dd HH:mm:ss}', '{UpdatedDate:yyyy-MM-dd HH:mm:ss}', {ArcherId}, '{MedicalRecordIds}')
            """;
    }
    public RecordReview(RecordReview old, string salesforceId)
    {
        if (old.StatusId != RecordReviewStatus.Error)
        {
            throw new InvalidOperationException("Cannot create a new review from a review that is not in error state");
        }

        Id = old.Id;
        CaseName = old.CaseName;
        MatterId = old.MatterId;
        InjuredPartyId = old.InjuredPartyId;
        InjuredPartyName = old.InjuredPartyName;
        StatusId = RecordReviewStatus.Unknown;
        SalesforceId = salesforceId;
        CreatedDate = old.CreatedDate;
        UpdatedDate = DateTime.UtcNow;
    }

    public virtual void MarkAccepted(Guid documentReviewId)
    {
        DocumentReviewId = documentReviewId;
        StatusId = RecordReviewStatus.Accepted;
        UpdatedDate = DateTime.UtcNow;
        Error = null;
    }

    public virtual void MarkAccepted(int archerId)
    {
        ArcherId = archerId;
        StatusId = RecordReviewStatus.Accepted;
        UpdatedDate = DateTime.UtcNow;
        Error = null;
    }

    public virtual void MarkInReview(string reviewDetails)
    {
        StatusId = RecordReviewStatus.InReview;
        UpdatedDate = DateTime.UtcNow;
        ReviewDetails = reviewDetails;
        Error = null;
    }

    public virtual void MarkReadyForReview()
    {
        StatusId = RecordReviewStatus.Accepted;
        UpdatedDate = DateTime.UtcNow;
        Error = null;
    }

    public virtual void MarkAwaitingRecords(string reviewDetails)
    {
        StatusId = RecordReviewStatus.AwaitingRecords;
        UpdatedDate = DateTime.UtcNow;
        ReviewDetails = reviewDetails;
        Error = null;
    }

    public virtual void AppealReview(List<string> medicalRecordIds, string? reviewDetails)
    {
        StatusId = RecordReviewStatus.Appeal;
        UpdatedDate = DateTime.UtcNow;
        ReviewDetails = reviewDetails;
        AppendMedicalRecordIds(medicalRecordIds);
    }

    public virtual void ContinueReview()
    {
        if (!ArcherId.HasValue && !DocumentReviewId.HasValue)
        {
            throw new InvalidOperationException("Cannot retry review that has not been started");
        }

        StatusId = RecordReviewStatus.InReview;
        UpdatedDate = DateTime.UtcNow;
        Error = null;
    }

    public virtual void CloseReview()
    {
        if (!ArcherId.HasValue && !DocumentReviewId.HasValue)
        {
            throw new InvalidOperationException("Cannot retry review that has not been started");
        }

        if (StatusId == RecordReviewStatus.CompletedReject ||
            StatusId == RecordReviewStatus.CompletedFile)
        {
            throw new InvalidOperationException("Cannot close review that is already completed");
        }

        StatusId = RecordReviewStatus.CompletedReject;
        UpdatedDate = DateTime.UtcNow;
        Error = null;
    }

    public virtual void NeedsFirmAttention(string reviewDetails)
    {
        if (!ArcherId.HasValue && !DocumentReviewId.HasValue)
        {
            throw new InvalidOperationException("Cannot mark review as needs firm attention if it has not been started");
        }

        ReviewDetails = reviewDetails;
        StatusId = RecordReviewStatus.NeedsFirmAttention;
        UpdatedDate = DateTime.UtcNow;
        Error = null;
    }

    public virtual void CompleteReview(string reviewDetails, RecordReviewStatus statusId)
    {
        if (!ArcherId.HasValue && !DocumentReviewId.HasValue)
        {
            throw new InvalidOperationException("Cannot complete review that has not been started");
        }

        ReviewDetails = reviewDetails;
        StatusId = statusId;
        UpdatedDate = DateTime.UtcNow;
        Error = null;
    }

    public virtual void SetError(string error)
    {
        StatusId = RecordReviewStatus.Error;
        Error = error;
        UpdatedDate = DateTime.UtcNow;
    }

    public virtual void SetMedicalRecordIds(List<string> recordIds)
    {
        var jsonString = JsonSerializer.Serialize(recordIds.Distinct());
        MedicalRecordIds = jsonString;
    }

    public virtual void AppendMedicalRecordIds(List<string> recordIds)
    {
        if (string.IsNullOrEmpty(MedicalRecordIds))
        {
            SetMedicalRecordIds(recordIds);
        }
        else
        {
            var initialList = JsonSerializer.Deserialize<List<string>>(MedicalRecordIds) ?? new List<string>();
            initialList.AddRange(recordIds);
            MedicalRecordIds = JsonSerializer.Serialize(initialList.Distinct());
        }
    }

    public virtual List<string> GetMedicalRecordIds()
    {
        if (string.IsNullOrEmpty(MedicalRecordIds))
        {
            return new List<string>();
        }
        return JsonSerializer.Deserialize<List<string>>(MedicalRecordIds) ?? new List<string>();
    }

    public virtual void SetSalesForceId(string newSalesForceId)
    {
        SalesforceId = newSalesForceId;
    }

}

public enum RecordReviewStatus
{
    Unknown = 1,
    Accepted = 7,
    InReview = 2,
    AwaitingRecords = 8,
    CompletedFile = 3,
    CompletedReject = 4,
    NeedsFirmAttention = 5,
    Error = 6,
    Appeal = 9,
}

