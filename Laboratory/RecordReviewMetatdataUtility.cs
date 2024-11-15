using Laboratory.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using Npgsql;
using System.Data;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Laboratory
{
    public class RecordReviewMetatdataUtility
    {

        static string DBConnectionString = "Connection String";
        private readonly HttpClient _httpClient;
        private readonly string BaseUri = "https://api.salesforce.com";
        private readonly string FileInfoPath = "/fileinfo";
        private readonly ILogger<RecordReviewMetatdataUtility> _logger;

        private static readonly JsonSerializerOptions IgnoreNullSerializationOption = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public RecordReviewMetatdataUtility(HttpClient httpClient, ILogger<RecordReviewMetatdataUtility> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async void AuditArcher()
        {
            List<MedicalRecord> medicalRecords = ParseCSVForMedialRecords();

            List<RecordReview> recordReviews = await GetRecordReviewsFromPostgres();

            List<ArcherAudit> archerAudits = MapArcherAudits(recordReviews, medicalRecords);

            //Ensure all docs sent to Archer are in "in review"
            //Ensure all docs sent to Archer are tracked in PostgreSQL

            var incompleteRecordReviews = archerAudits.Where(a => a.RecordReviewStatusId != 3 && a.RecordReviewStatusId != 4);
            var completedRecordReviews = archerAudits.Where(x => x.RecordReviewStatusId == 3 || x.RecordReviewStatusId == 4);

            List<IncompleteAuditReport> incompleteAudits = new List<IncompleteAuditReport>();
            List<CompleteAuditReport> completeAuditReports = new List<CompleteAuditReport>();
            foreach (var incompleteRecordReview in incompleteRecordReviews)
            {

                var incompleteAudit = await AuditIncompleteReview(incompleteRecordReview);
                if (incompleteAudit != null)
                {
                    incompleteAudits.Add(incompleteAudit);
                }
            }

            foreach (var completedRecordReview in completedRecordReviews)
            {
                //Ensure all completed reviews have corresponding BMHL files
                //Ensure all completed reviews have corresponding docs in status "review complete"

               var completeAudit = await AuditCompleteReview(completedRecordReview);
                if(completeAudit != null)
                {
                    completeAuditReports.Add(completeAudit);
                }
            }

            WriteReport(incompleteAudits, completeAuditReports);
        }

        private List<MedicalRecord> ParseCSVForMedialRecords()
        {
            // Configure the CSV reader
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                // Don't throw an exception if a CSV line has fewer fields than the header record
                MissingFieldFound = null
            };
            using (var reader = new StreamReader("sigmaReport.csv"))
            using (var csv = new CsvReader(reader, config))
            {
                // Get the records and map them to your custom object
                var records = csv.GetRecords<MedicalRecord>();
                return records.ToList();
            }
        }

        private async Task<List<RecordReview>> GetRecordReviewsFromPostgres() 
        {

            var connectionString = DBConnectionString;
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var command = new NpgsqlCommand("SELECT * FROM \"RecordReviews\"", connection);
            using (var reader = command.ExecuteReader())
            {
                List<RecordReview> recordReviews = new List<RecordReview>();

                while (reader.Read())
                {
                    recordReviews.Add(new RecordReview
                    {
                        Id = reader.GetGuid(0),
                        MatterId = reader.GetString(1),
                        InjuredPartyId = reader.GetString(2),
                        ReviewDetails = reader.GetString(3),
                        StatusId = reader.GetInt32(4),
                        Error = reader.GetString(5),
                        Updated = reader.GetDateTime(6),
                        SalesforceId = reader.GetString(7),
                        ArcherId = reader.GetInt32(8),
                        CaseName = reader.GetString(9),
                        Created = reader.GetDateTime(10),
                        INjuredPartyname = reader.GetString(11),
                        DocumentReviewId = reader.GetGuid(12),
                        MedicalRecordIds = reader.GetString(13),
                    });
                }

                return recordReviews;
            }
        }

        private List<ArcherAudit> MapArcherAudits(List<RecordReview> recordReviews, List<MedicalRecord> medicalRecords)
        {
            var archerAudits = new List<ArcherAudit>();
            foreach(var recordReview in recordReviews)
            {
                var medicalRecordsForMatter = medicalRecords.Where(x => x.MatterId == recordReview.MatterId).ToList();
                var audit = new ArcherAudit(recordReview, medicalRecordsForMatter);
                audit.SetMedicalRecordIds();
                archerAudits.Add(audit);
            }
            return archerAudits;
        }

        private async Task<CompleteAuditReport?> AuditCompleteReview(ArcherAudit completedRecordReview)
        {
            //check if files are in Completed Status

            var completedFiles = completedRecordReview.MedicalRecords.Where(x => x.FileName.Contains("Review Complete")).Select(x => x.FileInfoId).ToList();
            var notCompletedFiles = completedRecordReview.MedicalRecords.Where(x => !x.FileName.Contains("Review Complete")).Select(x => x.FileInfoId).ToList();


            //Get BookmarkHighlightedFiles

            var bmhlFiles = completedRecordReview.MedicalRecords.Where(x => x.FileName.Contains("BMHL")).Select(x => x.FileInfoId).ToList();

            var completeAudit = new CompleteAuditReport
            {
                MatterId = completedRecordReview.MatterId,
                NumberOfMedicalRecords = completedRecordReview.MedicalRecords.Count,
                NumberOfBMHLFiles = bmhlFiles.Count,
                MedicalRecordIds = completedRecordReview.MedicalRecords.Select(x=> x.MedicalReviewId).ToList(),
                BookmarkHighlightedFiles = bmhlFiles,
                BMHLFilesExist = bmhlFiles.Any()
            };

            return completeAudit;
        }

        private async Task<IncompleteAuditReport?> AuditIncompleteReview(ArcherAudit incompleteRecordReview)
        {

            //Check if all files are in review

            var medicalRecordIdsNotInReview = incompleteRecordReview.MedicalRecords.Where(x => x.FileInfoStatus != "In Review" && x.DocumentCreatedDate > incompleteRecordReview.RecordReviewCreatedDate).Select(x => x.FileInfoId).ToList();

            //Check if all files are tracked in PostgreSQL

            var medicalRecordIdsNotInPostgres = incompleteRecordReview.MedicalRecordIdsFromSigma.Except(incompleteRecordReview.MedicalRecordIdsFromPostgres).ToList();

            if(!medicalRecordIdsNotInReview.Any() && !medicalRecordIdsNotInPostgres.Any())
            {
                return null;
            }

            var incompleteAudit = new IncompleteAuditReport
            {
                MatterId = incompleteRecordReview.MatterId,
                MedicalRecordIdsMissingInPostgres = medicalRecordIdsNotInPostgres,
                MedicalRecordIdsUpdatedToCorrectStatus = medicalRecordIdsNotInReview
            };

            if (medicalRecordIdsNotInReview.Any())
            {
                ////Update Salesforce
                foreach (var medicalRecordId in medicalRecordIdsNotInReview)
                {
                    //await UpdateFileInfo()
                    await UpdateFileInfo(medicalRecordId);
                }
            }

            if (medicalRecordIdsNotInPostgres.Any())
            {
                ////Update Postgres
                incompleteRecordReview.RecordReview.AppendMedicalRecordIds(medicalRecordIdsNotInPostgres);
                await UpdatPostgres(incompleteRecordReview.RecordReview);
            }

            return incompleteAudit;
        }

        private async Task UpdateFileInfo(string medicalRecordId)
        {
            var response = await _httpClient.PatchAsJsonAsync(
                $"{BaseUri}{FileInfoPath}/{medicalRecordId}",
                new UpdateFileInfo(),
                IgnoreNullSerializationOption);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Failed to update review object status. StatusCode: {StatusCode}, Reason: {Reason}, Response: {ResponseContent}",
                    response.StatusCode,
                    response.ReasonPhrase,
                    await response.Content.ReadAsStringAsync());

                response.EnsureSuccessStatusCode();
            }
        }

        private async Task UpdatPostgres(RecordReview recordReview)
        {
            var connectionString = DBConnectionString;
            using var connection = new NpgsqlConnection(connectionString);
            var query = $"UPDATE \"RecordReviews\" SET \"MedicalRecordIds\" = '{recordReview.MedicalRecordIds}' WHERE \"Id\" = '{recordReview.Id}'";
            using var command = new NpgsqlCommand(query, connection);
            await command.ExecuteNonQueryAsync();
        }

        private void WriteReport(List<IncompleteAuditReport> incompleteAudits, List<CompleteAuditReport> completeAuditReports)
        {
            //Write to CSV
            WriteIncompleteAuditReport(incompleteAudits);
            WriteCompleteAuditReport(completeAuditReports); 
        }

        private async void WriteIncompleteAuditReport(List<IncompleteAuditReport> incompleteAudits)
        {
            //Write to CSV
            var csv = new StringBuilder();
            csv.AppendLine("MatterId,MedicalRecordIdsMissingInPostgres,MedicalRecordIdsUpdatedToCorrectStatus");
            foreach(var audit in incompleteAudits)
            {
                var line = $"{audit.MatterId},{string.Join(",", audit.MedicalRecordIdsMissingInPostgres)},{string.Join(",", audit.MedicalRecordIdsUpdatedToCorrectStatus)}";
                csv.AppendLine(line);
            }
            await File.WriteAllTextAsync("IncompleteAuditReport.csv", csv.ToString());
            Console.WriteLine("Incomplete Reviews Audit has been created.");

        }

        private async void WriteCompleteAuditReport(List<CompleteAuditReport> completeAuditReports)
        {
            //Write to CSV
            var csv = new StringBuilder();
            csv.AppendLine("MatterId,NumberOfMedicalRecords,NumberOfBMHLFiles,MedicalRecordIds,BookmarkHighlightedFiles,BMHLFilesExist");
            foreach (var audit in completeAuditReports)
            {
                var line = $"{audit.MatterId},{audit.NumberOfMedicalRecords},{audit.NumberOfBMHLFiles},{string.Join(",", audit.MedicalRecordIds)},{string.Join(",", audit.BookmarkHighlightedFiles)},{audit.BMHLFilesExist}";
                csv.AppendLine(line);
            }

            await File.WriteAllTextAsync("CompleteAuditReport.csv", csv.ToString());
            Console.WriteLine("Complete Reviews Audit has been created.");
        }
    }
    public class UpdateFileInfo
    {
        [JsonPropertyName("Status__c")]
        public string Status { get; }

        public UpdateFileInfo()
        {
            Status = "In Review";
        }
    }
}
