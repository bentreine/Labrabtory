using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using System.Data;


namespace Labrabtory;

public class RagicUtility
{
    private readonly LabratoryOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RagicUtility> _logger;
    private string? SalesforceAccessToken { get; set; }



    public RagicUtility(IOptions<LabratoryOptions> options, HttpClient httpClient, ILogger<RagicUtility> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task UpdateClientInformationOnRagic()
    {
        SalesforceAccessToken = await GetSalesForceToken();

        var SalesForceIds = await FetchSalesForceIds();
        foreach (var salesForceId in SalesForceIds)
        {

            _logger.LogInformation($"Checking Archer Id: {salesForceId.ArcherId}");
            var sfId = salesForceId.SalesforceRequestId.ToString();
            var matter = await GetMatter(sfId, salesForceId.CaseName, salesForceId.ArcherId);

            if (matter != null && ( matter.ClientId != matter.InjuredPartyId ))
            {
                _logger.LogInformation($"Patching Review with Archer Id: {salesForceId.ArcherId}");
                await PatchArcherReviews(matter);
            }
        }
    }

    private async Task<List<(Guid RequestId, string SalesforceRequestId, string CaseName, int ArcherId)>> FetchSalesForceIds()
    {
        using var connection = new NpgsqlConnection(_options.PostgresConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("SELECT \"Id\", \"MatterId\", \"CaseName\", \"ArcherId\" FROM \"RecordReviews\" WHERE \"ArcherId\" IS NOT NULL ORDER BY \"ArcherId\"", connection);
        using var reader = command.ExecuteReader();
        var results = new List<(Guid RequestId, string SalesforceRequestId, string CaseName, int ArcherId)>();
        while (await reader.ReadAsync())
        {
            results.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        }
        await connection.CloseAsync();

        //Selecting one of each Case to do an inital test, afterwards will remove to pass the whole data load
        var uniqueCases = results.GroupBy(item => item.CaseName).Select(group => group.First()).ToList();

        // return results;
        return uniqueCases;
    }

    private async Task<string> GetSalesForceToken()
    {
        //return <Prod Token>
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.SalesforceUri}/services/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.SalesforceClientId!,
                ["client_secret"] = _options.SalesforceClientSecret!
            })
        };

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch sf auth token");
            response.EnsureSuccessStatusCode();
        }
        var authPayload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return authPayload?["access_token"] ?? throw new InvalidOperationException("Failed to get Salesforce access token.");
    }

    private async Task<Matter> GetMatter(string requestId, string caseName, int archerId)
    {
        //Get Matter Information
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.SalesforceUri}/services/data/v60.0/sobjects/litify_pm__Matter__c/{requestId}");
        request.Headers.Add("Authorization", $"Bearer {SalesforceAccessToken}");
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch request");
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadAsStringAsync();
        var matterResultJson = JsonNode.Parse(result);

        var injuredPartyId = matterResultJson?["Injured_Party__c"]?.GetValue<string>();

        //Get Injured Party Information (Birth date and Date of Death)
        using var injuredPartyRequest = new HttpRequestMessage(HttpMethod.Get, $"{_options.SalesforceUri}/services/data/v60.0/sobjects/Account/{injuredPartyId}");

        injuredPartyRequest.Headers.Add("Authorization", $"Bearer {SalesforceAccessToken}");
        var injuredPartyResponse = await _httpClient.SendAsync(injuredPartyRequest);
        if (!injuredPartyResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch matter");
            injuredPartyResponse.EnsureSuccessStatusCode();
        }

        var injuredPartyResult = await injuredPartyResponse.Content.ReadAsStringAsync();
        var injuredResultJson = JsonNode.Parse(injuredPartyResult);

        try
        {
            return new Matter
            {
                Id = matterResultJson?["Id"]?.GetValue<string>() ?? throw new InvalidOperationException("Could not parse ID from created matter"),
                ClientId = matterResultJson?["litify_pm__Client__c"]?.GetValue<string>() ?? throw new InvalidOperationException("Could not parse ID from created matter"),
                InjuredPartyId = matterResultJson?["Injured_Party__c"]?.GetValue<string>() ?? throw new InvalidOperationException("Could not parse ID from created matter"),

                ClientFirstName = matterResultJson?["Client_First_Name__c"]?.GetValue<string>() ?? throw new InvalidOperationException("Could not First Name from created Matter"),
                ClientLastName = matterResultJson?["Client_Last_Name__c"]?.GetValue<string>() ?? throw new InvalidOperationException("Could not First Name from created Matter"),
                InjuredPartyFirstName = matterResultJson?["Injured_Party_First_Name__c"]?.GetValue<string>() ?? throw new InvalidOperationException("Could not First Name from created Matter"),
                InjuredPartyLastName = matterResultJson?["Injured_Party_Last_Name__c"]?.GetValue<string>() ?? throw new InvalidOperationException("Could not First Name from created Matter"),

                InjuredPartyDateOfBirth = injuredResultJson["litify_pm__Date_of_birth__c"].GetValue<string>() ?? throw new InvalidOperationException("Could Not parse Date of Birth"),
                InjuredPartyDateOfDeath = injuredResultJson["Date_of_Death__c"].GetValue<string>() ?? throw new InvalidOperationException("Could Not parse Date of Death"),

                ArcherId = archerId,
                CaseName = caseName
            };
        }catch(Exception ex)
        {
            _logger.LogError($"could not retreive matter information for {requestId}");
            return null;
        }
    }

    public async Task PatchArcherReviews(Matter matter) 
    {
        ArcherCaseMappings.TryGetValue(matter.CaseName, out var mapping);
        if (mapping != null)
        {
            await PatchRagic(matter, mapping);
        }
    }

    public async Task PatchRagic(Matter matter, RagicConfiguration mapping)
    {
        string apiUrl = $"https://na4.ragic.com/KellerPostman/{mapping.SheetName}/{matter.ArcherId}?api";

        using (HttpClient client = new HttpClient())
        {
            string authInfo = "<RagicDeveloperToken>"; //Ragic Auth token
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authInfo);

            var body = constructRagicBody(matter, mapping);
            var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogTrace($"Successfully updated matterId: {matter.Id}");
            }
            else
            {
                _logger.LogTrace($"{response.StatusCode}");
                _logger.LogTrace($"Failed to update the following matterId: {matter.Id}");
            }
        }
    }

    private JsonObject constructRagicBody(Matter matter, RagicConfiguration mapping)
    {
        var jsonObject = new JsonObject
        {
            [mapping.InjuredPartyFirstNameQuestionId.ToString()] = matter.InjuredPartyFirstName.ToString(),
            [mapping.InjuredPartyLastNameQuestionId.ToString()] = matter.InjuredPartyLastName.ToString(),
            [mapping.InjuredPartyBirthDayQuestionId.ToString()] = matter.InjuredPartyDateOfBirth.ToString(),
            [mapping.InjuredPartyDateOfDeathQuestionId.ToString()] = matter.InjuredPartyDateOfBirth.ToString(),
        };
        return jsonObject;
    }

    private bool ClientIsInjuredParty(string clientFirstName, string clientLastName, string injuredPartyFirstName, string injuredPartyLastName)
    {
        return (clientFirstName == injuredPartyFirstName) && (clientLastName == injuredPartyLastName);
    }

    private static readonly Dictionary<string, RagicConfiguration> ArcherCaseMappings = new Dictionary<string, RagicConfiguration>()
    {
        
        {"Acetaminophen Use During Pregnancy", new RagicConfiguration("dev-nec/3", "1000507",  "1000541", "1000543", "1000545", "1000555") }, //Used in lower environments for testing only
                                                //Sheet Name //MatterId //FirstName //Last Name //Birthday //Date of Death
        {"NEC Infant Formula", new RagicConfiguration("nec/8", "1001146",  "1001181", "1001183", "1001185", "1001338") },
        {"Zantac Pharmaceutical Use", new RagicConfiguration("zantac/1", "1000862",  "1000897", "1000899", "1000864", "1000904") },
        {"Camp Lejeune Exposure", new RagicConfiguration("clj/3", "1001757",  "1001546", "1001550", "1001553", "1001556" ) },
        {"Talcum Powder Exposure", new RagicConfiguration("talc/1", "1001962",  "1001979", "1001982", "1001985", "1001988") }
    };
}
public record Matter
{
    public string Id { get; set; }
    public string ClientId { get; set; }
    public string InjuredPartyId { get; set; }

    public string ClientFirstName { get; set; }

    public string ClientLastName { get; set; }

    public string InjuredPartyFirstName { get; set; }

    public string InjuredPartyLastName { get; set; }

    public string InjuredPartyDateOfBirth { get; set;  }

    public string InjuredPartyDateOfDeath { get; set; }

    public string CaseName { get; set; }

    public int ArcherId { get; set; }
}

public record RagicConfiguration(string SheetName, 
    string MatterQuestionId, 
    string InjuredPartyFirstNameQuestionId, 
    string InjuredPartyLastNameQuestionId, 
    string InjuredPartyBirthDayQuestionId,
    string InjuredPartyDateOfDeathQuestionId);

