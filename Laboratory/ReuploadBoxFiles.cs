using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Laboratory;

public class ReuploadBoxFiles
{
    private readonly IBoxClient boxClient;

    private readonly IDocumentStoreClient documentStoreClient;

    public ReuploadBoxFiles(IBoxClient boxClient, IDocumentStoreClient documentStoreClient)
    {
        this.boxClient = boxClient;
        this.documentStoreClient = documentStoreClient;
    }
    public async Task ReuploadFiles()
    {

        var filesToUpload = ParseCSVForFilesToUpload();

        foreach(var fileToUpload in filesToUpload)
        {
            var ids = fileToUpload.GetMedicalRecordIds();
            var documents = await documentStoreClient.GetDocuments(ids);
            await boxClient.UploadMedicalRecordsToBox("CLJ", fileToUpload.MatterId, fileToUpload.InjuredPartyName, documents, false);
        }
    }

    private List<FilesToUpload> ParseCSVForFilesToUpload()
    {
            // Configure the CSV reader
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                // Don't throw an exception if a CSV line has fewer fields than the header record
                MissingFieldFound = null
            };
            using (var reader = new StreamReader("CLJReupload.csv"))
            using (var csv = new CsvReader(reader, config))
            {
                // Get the records and map them to your custom object
                var records = csv.GetRecords<FilesToUpload>();
                return records.ToList();
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