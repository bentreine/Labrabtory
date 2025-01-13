using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Laboratory;
using Microsoft.Extensions.Logging;

public class ReuploadBoxFiles
{
    private readonly IBoxClient boxClient;

    private readonly IDocumentStoreClient documentStoreClient;

    private readonly ILogger<ReuploadBoxFiles> _logger;

    public ReuploadBoxFiles(IBoxClient boxClient, IDocumentStoreClient documentStoreClient, ILogger<ReuploadBoxFiles> logger)
    {
        this.boxClient = boxClient;
        this.documentStoreClient = documentStoreClient;
        _logger = logger;
    }
    public async Task ReuploadFiles()
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