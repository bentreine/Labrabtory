using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Laboratory
{
    public class WeeklyArcherAudit
    {
        private static List<string> caseUrls = new List<string> { "nec/8", "zantac/1", "clj/3", "talc/1" };
        private readonly HttpClient _httpClient;
        private readonly ILogger<WeeklyArcherAudit> _logger;

        private string? SalesforceAccessToken { get; set; } = "";

        public WeeklyArcherAudit(HttpClient httpClient, ILogger<WeeklyArcherAudit> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task RunWeeklyArcherAudit()
        {

            var ragicReviews = new List<RagicReview>();
            foreach (var url in caseUrls)
            {
                var ragicReview = await GetRagicReviews(url);
                ragicReviews.AddRange(ragicReview);
            }
        }

        private async Task<List<RagicReview>> GetRagicReviews(string caseUrl)
        {
            List<RagicReview> reviews = new();
            int offset = 0;

            List<RagicReview> data;
            do
            {
              var url = $"https://na4.ragic.com/KellerPostman/{caseUrl}?api&v=3&offset={offset}";
              data = await QueryRagic(url, caseUrl);
                reviews.AddRange(data);


                    offset += 1000;
                } while (data.Count > 0);
            return reviews;
        }

        private async Task<List<RagicReview>> QueryRagic(string url, string caseUrl)
        {
            var recommendationKey = caseUrl == "nec/8" ? "Recommendations" : "Recomendation";
            var matterKey = caseUrl == "nec/8" ? "Project Number" : "Project ID";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Basic {SalesforceAccessToken}");
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch request");
                response.EnsureSuccessStatusCode();
            }

            var result = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(result);
            var records = new List<RagicReview>();

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                var record = property.Value;



                records.Add(new RagicReview
                (
                    record.GetProperty(matterKey).GetString(),
                     record.GetProperty("Claim Status").GetString(),
                    record.GetProperty(recommendationKey).GetString()
                ));
            }


            return records;
        }

    }

    public class RagicReview
    {
        public string ProjectNumber { get; set; }
        public string ClaimStatus { get; set; }
        public string Recommendations { get; set; }

        public RecordReviewStatus MappedSalesforceStatus { get; init; }

        private const string ClaimStatus_InReview = "In Review";
        private const string ClaimStatus_NeedsFirmAttention = "Needs Firm Attention";
        private const string ClaimStatus_Complete = "Complete";
        private const string ClaimStatus_Unreviewed = "Unreviewed";
        private const string ClaimStatus_AwaitingRecords = "Awaiting Records";
        private const string ClaimStatus_ReadyForQcReview = "Ready for QC Review";
        private const string ClaimStatus_Closed = "Closed";
        private const string ClaimStatus_CompleteAwaitingUpload = "Complete - Awaiting Upload";
        private const string Recommendation_Reject = "Reject";
        private const string Recommendation_File = "File";
        private const string Recommendation_NeedsFirmAttention = "Hold/Need Additional Records";
        private const string Recommendation_Bellwether = "Bellwether Quality Case";


        public RagicReview(string ProjectNumber, string ClaimStatus, string Recommendation)
        {
            this.ProjectNumber = ProjectNumber;
            this.ClaimStatus = ClaimStatus;
            this.Recommendations = Recommendation;




            MappedSalesforceStatus = ClaimStatus switch
            {
                ClaimStatus_InReview => RecordReviewStatus.InReview,
                ClaimStatus_NeedsFirmAttention => Recommendation switch
                {
                    Recommendation_Reject => RecordReviewStatus.CompletedReject,
                    Recommendation_File => RecordReviewStatus.CompletedFile,
                    Recommendation_NeedsFirmAttention => RecordReviewStatus.NeedsFirmAttention,
                    Recommendation_Bellwether => RecordReviewStatus.CompletedFile,
                    _ => RecordReviewStatus.NeedsFirmAttention
                },
                ClaimStatus_Complete => Recommendation switch
                {
                    Recommendation_Reject => RecordReviewStatus.CompletedReject,
                    Recommendation_File => RecordReviewStatus.CompletedFile,
                    Recommendation_NeedsFirmAttention => RecordReviewStatus.NeedsFirmAttention,
                    Recommendation_Bellwether => RecordReviewStatus.CompletedFile,
                    _ => RecordReviewStatus.NeedsFirmAttention
                },
                ClaimStatus_Unreviewed => RecordReviewStatus.Accepted,
                ClaimStatus_AwaitingRecords => RecordReviewStatus.AwaitingRecords,
                ClaimStatus_Closed => RecordReviewStatus.CompletedReject,
                ClaimStatus_CompleteAwaitingUpload => RecordReviewStatus.InReview,
                ClaimStatus_ReadyForQcReview => RecordReviewStatus.InReview,
                _ => RecordReviewStatus.Unknown
            };



        }

        public enum RecordReviewStatus
        {
            Unknown = 1,
            Accepted = 7,
            InReview = 2,
            AwaitingRecords = 8,
            CompletedFile = 3,
            CompletedReject = 4,
            NeedsFirmAttention = 5,
            Error = 6,
            Appeal = 9
        }
    }
}
