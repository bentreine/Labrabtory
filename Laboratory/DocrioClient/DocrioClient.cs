using System.Web;
using Laboratory;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

public class DocrioClient(
    HttpClient client,
    ILogger<DocrioClient> logger,
    IOptions<DocrioClientOptions> options)
 : IDocrioClient
{

    private readonly HttpClient _httpClient = client;
    private readonly ILogger<DocrioClient> _logger = logger;
    private readonly DocrioClientOptions _options = options.Value;

    public async Task<Dictionary<string, Uri>> GetDocumentUrls(
    List<string> ids,
    CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        var bearerToken = "TodoRealToken";
        var baseUrl = "https://api.990483905850.genesisapi.com/v1";
        query["Ids"] = string.Join(",", ids);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
        var response = await _httpClient.GetAsync($"{baseUrl}/files?{query}");

        if (!response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Failed to retrieve document {id} from Docrio. Status code: {StatusCode}. Reason: {ReasonPhrase}. Message: {Message}",
                ids,
                response.StatusCode,
                response.ReasonPhrase,
                result);

            response.EnsureSuccessStatusCode();
        }

        if (response.Content.Headers.ContentLength == 0)
        {
            _logger.LogError("Response Content for document Ids: {id} is null", ids);
            return [];
        }

        var documents = await response.Content
         .ReadFromJsonAsync<IDictionary<string, IList<DocrioDocumentMetadata>>>();

        var documentUrls = documents!.ContainsKey("Records") ? documents["Records"]?.ToDictionary(x => x.Id, x => x.SignedUrl) : null;

        if (documentUrls.IsNullOrEmpty())
        {
            _logger.LogWarning("Could not find document URL(s) for document(s) with Id(s) {DocumentId}", ids);
            return [];
        }

        var notFound = ids.Except(documentUrls!.Select(x => x.Key)).ToList();

        if (notFound.Count != 0)
        {
            _logger.LogWarning("{IdsCount} was passed and only {DocumentUrlsCount} was returned", ids.Count, documentUrls!.Count);
            notFound.ForEach(x => _logger.LogWarning("Medical record not found with id {Id}", x));
        }

        return documentUrls!;
    }
}

public class DocrioClientOptions
{
    public Uri BaseUri { get; set; }
}

public record DocrioDocumentMetadata(
    Uri SignedUrl,
    string Id);
