using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Laboratory.ArcherClient
{
    public class ArcherClient: IArcherClient
    {

        private readonly HttpClient _httpClient;
        private readonly ILogger<ArcherClient> _logger;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly string _baseAddress;

        public static readonly Dictionary<string, string> CaseTypeMapping = new()
    {
        { "Acetaminophen Use During Pregnancy", "APAP" },
        { "NEC Infant Formula", "NEC" },
        { "Zantac Pharmaceutical Use", "Zantac" },
        { "Camp Lejeune Exposure", "clj" },
        { "Talcum Powder Exposure", "Talc" }
    };
        private readonly static Dictionary<string, string> ArcherWebNavigationKey = new()
    {
        { "dev-nec", "3" },
        { "nec", "8" },
        { "zantac", "1" },
        { "clj", "3" },
        { "talc", "1" }
    };


        public ArcherClient(
    HttpClient httpClient,
    IOptions<ArcherClientOptions> options,
    ILogger<ArcherClient> logger,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration)
        {
            var archerAPIKey = configuration.GetSection("AppSettings")["ArcherApiKey"];
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", archerAPIKey);
            _logger = logger;
            _baseAddress = "https://na4.ragic.com/KellerPostman";
        }


        public async Task<int> CreateNewReview(CreateNewArcherReviewRequest archerRequest)
        {
            var caseName = GetCaseName(archerRequest.CaseName, _hostEnvironment).ToLowerInvariant(); //Make sure this is okay
            var archerWebNavigationKey = ArcherWebNavigationKey[caseName]; //Make sure this is okay
            var url = $"{_baseAddress}/{caseName}/{archerWebNavigationKey}?api=true";

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(BuildCreateNewReviewRequest(archerRequest.CaseName, archerRequest))
            };
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                response.EnsureSuccessStatusCode();
            }

            var resultJson = JsonNode.Parse(result);
            if (resultJson == null || resultJson!["status"]?.GetValue<string>() != "SUCCESS")
            {
                throw new InvalidOperationException("Failed to create new review in Archer.");
            }

            var id = resultJson!["ragicId"]?.GetValue<int>();
            if (!id.HasValue)
            {
                throw new InvalidOperationException("Created review in Archer however ragic id is missing.");
            }

            return id.Value;
        }

        public static string GetCaseName(string caseName, IHostEnvironment hostEnvironment)
        {
            if (!CaseTypeMapping.TryGetValue(caseName, out var mappedCaseName))
            {
                throw new InvalidOperationException($"CaseName {caseName} is not configured with Archer.");
            }

            return mappedCaseName;
        }

        private List<KeyValuePair<string, string>> BuildCreateNewReviewRequest(string caseName, CreateNewArcherReviewRequest request) =>
       GetCaseName(caseName, _hostEnvironment).ToLowerInvariant() switch
       {
           "dev-nec" => [
               new ("1000507", request.MatterId),
                new ("1000508", request.MatterName),
                new ("1000540", request.ClientFirstName),
                new ("1000542", request.ClientLastName),
                new ("1000544", request.ClientPhoneNumber ?? string.Empty),
                new ("1000546", request.ClientEmail ?? string.Empty),
                new ("1000541", request.ClientFirstName),
                new ("1000543", request.ClientLastName),
                new ("1000545", request.ClientDateOfBirth?.ToString("yyyy-MM-dd") ?? ""),
                new ("1000511", $"{request.InjuredPartyFirstName} {request.InjuredPartyLastName}"),
                new ("1000547", request.InjuredPartyDateOfBirth?.ToString("yyyy-MM-dd") ?? ""),
                new ("1000520", "Yes"),
                new ("1000526", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
               ],
           "nec" => [
               new ("1001146", request.MatterId),
                new ("1001147", request.MatterName),

                // KP Client == Archer Rep
                new ("1001180", request.ClientFirstName),
                new ("1001182", request.ClientLastName),
                new ("1001184", request.ClientPhoneNumber ?? string.Empty),
                new ("1001186", request.ClientEmail ?? string.Empty),

                // KP IP == Archer Client
                new ("1001181", request.InjuredPartyFirstName),
                new ("1001183", request.InjuredPartyLastName),
                new ("1001185", request.InjuredPartyDateOfBirth?.ToString("yyyy-MM-dd") ?? ""),

                new ("1001158", "Yes"),
                new ("1001164", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
               ],
           "zantac" => [
               new ("1000862", request.MatterId),
                new ("1000892", request.MatterName),

                // KP Client == Archer Rep
                new ("1000896", request.ClientFirstName),
                new ("1000898", request.ClientLastName),
                new ("1000866", request.ClientPhoneNumber ?? string.Empty),
                new ("1000867", request.ClientEmail ?? string.Empty),

                // KP IP == Archer Client
                new ("1000897", request.InjuredPartyFirstName),
                new ("1000899", request.InjuredPartyLastName),
                new ("1000864", request.InjuredPartyDateOfBirth?.ToString("yyyy-MM-dd") ?? ""),
                new ("1000904", request.InjuredPartyDateOfDeath?.ToString("yyyy-MM-dd") ?? ""),

                new ("1000926", "Yes"), //Docs received
                new ("1000925", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")) //Latest Docs received date
           ],
           "clj" => [
               new ("1001757", request.MatterId),
                new ("1001531", request.MatterName),

                // KP Client == Archer Rep
                new ("1001545", request.ClientFirstName),
                new ("1001549", request.ClientLastName),
                new ("1001552", request.ClientPhoneNumber ?? string.Empty),
                new ("1001555", request.ClientEmail ?? string.Empty), 

                // KP IP == Archer Client
                new ("1001546", request.InjuredPartyFirstName),
                new ("1001550", request.InjuredPartyLastName),
                new ("1001553", request.InjuredPartyDateOfBirth?.ToString("yyyy-MM-dd") ?? ""),
                new ("1001556", request.InjuredPartyDateOfDeath?.ToString("yyyy-MM-dd") ?? ""),

                new ("1001548", "Yes"), //Docs received
                new ("1001547", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")) //Latest Docs received date
           ],
           "talc" => [
               new ("1001962", request.MatterId),
                new ("1001963", request.MatterName),

                // KP Client == Archer Rep
                new ("1001978", request.ClientFirstName),
                new ("1001981", request.ClientLastName),
                new ("1001984", request.ClientPhoneNumber ?? string.Empty),
                new ("1001987", request.ClientEmail ?? string.Empty),

                // KP IP == Archer Client
                new ("1001979", request.InjuredPartyFirstName),
                new ("1001982", request.InjuredPartyLastName),
                new ("1001985", request.InjuredPartyDateOfBirth?.ToString("yyyy-MM-dd") ?? ""),
                new ("1001988", request.InjuredPartyDateOfDeath?.ToString("yyyy-MM-dd") ?? ""),

                new ("1001986", "Yes"), // Docs received
                new ("1001980", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")) // Latest Docs received date
          ],
           _ => throw new NotImplementedException($"Case {caseName} is not configured with Archer yet")
       };
    }
}
