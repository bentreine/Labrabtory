using Box.Sdk.Gen;
using KellerPostman.Salesforce.Sdk.Salesforce;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Box.Sdk.Gen.Schemas;
using System.Net.Http.Headers;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace Laboratory.SalesforceClient
{
    public class LocalSalesforceClient: ILocalSalesforceClient
    {
        public ILogger<LocalSalesforceClient> _logger;
        private const string FileInfoPath = "/services/data/v60.0/sobjects/litify_docs__File_Info__c";
        private string BaseUri = "https://kellerlenkner2.my.salesforce.com";
        private string ClientId = "";
        private string ClientSecret = "";

        private string? _accessToken;
        private DateTime _accessTokenExpiry;
        private readonly HttpClient _httpClient = new();

        public LocalSalesforceClient(ILogger<LocalSalesforceClient> logger, IConfiguration configuration)
        {
            _logger = logger;
            ClientId = configuration.GetSection("AppSettings")["SalesforceClientId"];
            ClientSecret = configuration.GetSection("AppSettings")["SalesforceClientSecret"];

        }

        public async Task UpdateDocumentFileInfo(string medicalRecordId, FileInfoStatus updateStatus)
        {
            await EnsureAuthenticatedAsync();

            var response = await _httpClient.PatchAsJsonAsync(
                $"{BaseUri}{FileInfoPath}/{medicalRecordId}",
                new UpdateFileInfo(updateStatus));

            if (response.StatusCode.Equals(HttpStatusCode.BadRequest))
            {
                _logger.LogError(
                "Failed to update document status. StatusCode: {StatusCode}, Reason: {Reason}, Response: {ResponseContent}",
                response.StatusCode,
                response.ReasonPhrase,
                await response.Content.ReadAsStringAsync());
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                "Failed to update document status. StatusCode: {StatusCode}, Reason: {Reason}, Response: {ResponseContent}",
                    response.StatusCode,
                response.ReasonPhrase,
                    await response.Content.ReadAsStringAsync());
            }
        }


        private async Task EnsureAuthenticatedAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _accessTokenExpiry)
                return;

            var tokenEndpoint = "https://login.salesforce.com/services/oauth2/token";
            var content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("client_secret", ClientSecret),
        });

            var response = await _httpClient.PostAsync(tokenEndpoint, content);
            response.EnsureSuccessStatusCode();

            var tokenJson = await response.Content.ReadAsStringAsync();
            var tokenObj = JsonSerializer.Deserialize<JsonElement>(tokenJson);
            _accessToken = tokenObj.GetProperty("access_token").GetString();
            int expiresIn = tokenObj.GetProperty("expires_in").GetInt32();
            _accessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // buffer for clock skew

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }

    }

    public class UpdateFileInfo
    {
        [JsonPropertyName("Status__c")]
        public string Status { get; }

        public UpdateFileInfo(FileInfoStatus? updateStatus)
        {
            switch (updateStatus)
            {
                case FileInfoStatus.InReview:
                    Status = "In Review";
                    break;
                case FileInfoStatus.ReviewComplete:
                    Status = "Review Complete";
                    break;
                default:
                    Status = "--None--";
                    break;
            }
        }
    }
}
