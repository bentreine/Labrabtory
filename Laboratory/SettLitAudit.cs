using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Box.Sdk.Gen.Internal;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;

namespace Laboratory;

public class SettLitAudit
{
    private readonly HttpClient _httpClient;

    public SettLitAudit(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task AuditSettLit()
    {
        StringBuilder mismatchedData = new StringBuilder();
        mismatchedData.AppendLine("MatterId, Expected Status (In Postgres), Actual Status (From SettLit)");
        StringBuilder matchedData = new StringBuilder();

        Console.WriteLine("Starting SettLit Audit");
        Console.WriteLine("Parsing CSV file");
        var settLitInfo = ParseCSV("SettLitAudit.csv");
        Console.WriteLine($"Total Records found: {settLitInfo.Count}");
        var mismatchedCount = 0;
        var matchedCount = 0;
        foreach (var settLitRequest in settLitInfo)
        {
            var settLitData = await GetSettLitData(settLitRequest.VendorRequestId);
            var status = GetStatus(settLitRequest.Status);
            if (settLitData.QueryStatus != status)
            {
                Console.WriteLine("Mismatched Data found");
                mismatchedData.AppendLine($"{settLitRequest.MatterId}, {status}, {settLitData.QueryStatus}");
                mismatchedCount++;
                await SendToMedRecords(new SettLiTWebhookRequest()
                {
                    EventName = "Query Status Updated",
                    ClientId = settLitRequest.VendorRequestId,
                    ClientStatus = settLitData.ClientStatus,
                    QueryStatus = settLitData.QueryStatus,
                    EnricherStatus = settLitData.EnricherStatus,
                    DataCount = settLitData.dataCount
                });

                var test = new SettLiTWebhookRequest()
                {
                    EventName = "Query Status Updated",
                    ClientId = settLitRequest.VendorRequestId,
                    ClientStatus = settLitData.ClientStatus,
                    QueryStatus = settLitData.QueryStatus,
                    EnricherStatus = settLitData.EnricherStatus,
                    DataCount = settLitData.dataCount
                };
                var test2 = JsonConvert.SerializeObject(test);


                var writeLine = JsonConvert.SerializeObject(settLitData);
                Console.WriteLine(writeLine);
            }
            else
            {

                matchedData.AppendLine(settLitRequest.MatterId);
                matchedCount++;
            }
            //Log Percent complete
            double completePercentage = ((double)mismatchedCount + (double)matchedCount) * 100.00 / (double)settLitInfo.Count;
            Console.WriteLine($"Percent Complete: {completePercentage}%");
        }
        Console.WriteLine($"Mismatched Data:{mismatchedCount} found of {settLitInfo.Count}");
        Console.WriteLine($"Matched Data:{matchedCount} found of {settLitInfo.Count}");

        File.WriteAllText("MismatchedData.csv", mismatchedData.ToString());
        File.WriteAllText("MatchedData.csv", matchedData.ToString());
    }

    private async Task SendToMedRecords(SettLiTWebhookRequest message)
    {
        var json = JsonConvert.SerializeObject(message);
        var content = new StringContent(json, Encoding.UTF8, "application/json");


        var response = await _httpClient.PostAsync("https://api.kellerpostman.com/medicalrecords/api/v1/medical-data-requests/settlit-webhook?subscription-key=4504fad257d04f9cb968fa1ed03a985a", content); //TODO Point to Med Records and use Subscription Key

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("Failed to send data to MedRecords");
        }
    }

    private async Task<SettLitData> GetSettLitData(string vendorRequestId)
    {
        // Call the API to get the data
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://settlit.marbleapi.com/external/query/{vendorRequestId}");
        request.Headers.Add("api-key", "wufojhdfgasfda12blBLp0jLSDFh934--fL3GHTDbp08lcS0O94xK");
        request.Headers.Add("accept", "application/json");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get data for vendorRequestId: {vendorRequestId}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var data = JsonConvert.DeserializeObject<SettLitData>(responseContent);

        return data;
    }

    public class SettLiTWebhookRequest
    {
        /// <summary>
        /// "Enricher Status Updated", "Query Status Updated",
        /// "Data Count Updated", or "Data Export Completed"
        /// </summary>
        [JsonPropertyName("eventName")]
        public required string EventName { get; set; }

        [JsonPropertyName("clientId")]
        public required string ClientId { get; set; }

        /// <summary>
        /// "Incomplete Profile" or "Complete Profile"
        /// </summary>
        [JsonPropertyName("clientStatus")]
        public string? ClientStatus { get; set; }

        /// <summary>
        /// "Processing", "Completed No Results", or "Completed"
        /// </summary>
        [JsonPropertyName("enricherStatus")]
        public string? EnricherStatus { get; set; }

        /// <summary>
        /// "Pending", "Processing", or "Complete"
        /// </summary>
        [JsonPropertyName("queryStatus")]
        public string? QueryStatus { get; set; }

        [JsonPropertyName("dataCount")]
        public int? DataCount { get; set; }

        [JsonPropertyName("exportJobId")]
        public string? ExportJobId { get; set; }

        private bool HasResults => DataCount.GetValueOrDefault() > 0;
    }


    private string GetStatus(int status)
    {
        switch (status)
        {
            case 0:
                return "Pending";
            case 1:
                return "Incomplete Profile";
            case 2:
                return "No Results";
            case 3:
                return "Completed";
            case 4:
                return "Data Export Completed";
            default:
                return "Unknown";
        }
    }

    private List<SettLitInfo> ParseCSV(string csvFilePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Don't throw an exception if a CSV line has fewer fields than the header record
            MissingFieldFound = null
        };
        using (var reader = new StreamReader(csvFilePath))
        using (var csv = new CsvReader(reader, config))
        {
            // Get the records and map them to your custom object
            var records = csv.GetRecords<SettLitInfo>();
            return records.ToList();
        }
    }

    private class SettLitData
    {
        public string ClientId { get; set; }
        public string ClientStatus { get; set; }
        public string EnricherStatus { get; set; }
        public string QueryStatus { get; set; }
        public int dataCount { get; set; }

    }

    private class SettLitInfo
    {
        public string MatterId { get; set; }
        public string VendorRequestId { get; set; }

        /// <summary>
        /// 0 - Initiated
        /// 1 - Incomplete Profile
        /// 2 - No Results
        /// 3 - Completed
        /// 4 - Data Export Completed
        /// </summary>
        public int Status { get; set; }
    }
}
