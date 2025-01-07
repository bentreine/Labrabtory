public interface IBoxClient
{
    Task UploadMedicalRecordsToBox(string caseName,
        string matterId,
        string injuredPartyName,
        List<(string TempFilePath, string SalesforceDocumentId)> filePaths,
        bool isAdditional = false);
}