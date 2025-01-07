public interface IDocrioClient
{
    public Task<Dictionary<string, Uri>> GetDocumentUrls(List<string> ids, CancellationToken cancellationToken = default);
}
