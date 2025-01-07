namespace Laboratory;

public interface IDocumentStoreClient
{
      public Task<List<(string TempFilePath, string SalesforceDocumentId)>> GetDocuments(List<string> ids, CancellationToken cancellationToken = default);

}
