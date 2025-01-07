using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IO.Abstractions;
using HeyRed.Mime;

namespace Laboratory;

public class DocumentStoreClient : IDocumentStoreClient
{

    private readonly IDocrioClient _docrioClient;
    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<DocumentStoreClient> _logger;

    public DocumentStoreClient(IDocrioClient docrioClient, HttpClient httpClient, IFileSystem fileSystem, ILogger<DocumentStoreClient> logger)
    {
        _docrioClient = docrioClient;
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<List<(string TempFilePath, string SalesforceDocumentId)>> GetDocuments(List<string> ids, CancellationToken cancellationToken = default)
    {
        if (ids.IsNullOrEmpty())
        {
            throw new ArgumentNullException(nameof(ids));
        }

        List<(string TempFilePath, string SalesforceDocumentId)> fileNames = [];

        const int chunkSize = 5;
        var chunks = ChunkList(ids, chunkSize);

        foreach (var chunk in chunks)
        {
            var urls = await _docrioClient.GetDocumentUrls(chunk, cancellationToken);
            if (urls.IsNullOrEmpty())
            {
                _logger.LogWarning("Could not find document URLs for documents with IDs {DocumentId}", ids);
                break;
            }

            foreach (var url in urls)
            {
                // Specify ResponseHeadersRead to avoid loading the entire response into memory, and failing for large files
                var response = await _httpClient.GetAsync(url.Value, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Failed to get document from Docrio. StatusCode: {StatusCode}, Reason: {Reason}, Response: {ResponseContent}",
                        response.StatusCode,
                        response.ReasonPhrase,
                        await response.Content.ReadAsStringAsync(cancellationToken));
                    response.EnsureSuccessStatusCode();
                }

                using var result = await response.Content.ReadAsStreamAsync(cancellationToken);
                var extension = MimeTypesMap.GetExtension(response.Content.Headers.ContentType?.ToString());
                var fileName = Path.Combine(_fileSystem.Path.GetTempPath(), $"{Guid.NewGuid()}.{extension}");
                using var fileStream = _fileSystem.File.Create(fileName);
                await result.CopyToAsync(fileStream, cancellationToken);

                fileNames.Add((fileName, url.Key));
            }
        }

        return fileNames;
    }

    private static IEnumerable<List<string>> ChunkList(List<string> list, int chunkSize)
    {
        for (int i = 0; i < list.Count; i += chunkSize)
        {
            yield return list.Skip(i).Take(chunkSize).ToList();
        }
    }
}
