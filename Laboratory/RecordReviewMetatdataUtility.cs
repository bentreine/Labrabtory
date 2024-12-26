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

        static string DBConnectionString = "Server=kp-core-psql-prod.postgres.database.azure.com;Database=medicalrecordsdb;User Id=kpsqladmin;Password=b6Nm]oQgu*x};";
        private readonly HttpClient _httpClient;
        private readonly string BaseUri = "https://kellerlenkner2.my.salesforce.com";
        private readonly string FileInfoPath = "/services/data/v60.0/sobjects/litify_docs__File_Info__c";
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

        public async Task AuditArcher()
        {
            _logger.LogInformation("Starting Archer Audit");
            _logger.LogInformation("Parsing CSV for Medical Records");
            List<MedicalRecord> medicalRecords = ParseCSVForMedialRecords();

            List<RecordReview> recordReviews = await GetRecordReviewsFromPostgres();

            _logger.LogInformation("Mapping Archer Audits");    
            List<ArcherAudit> archerAudits = MapArcherAudits(recordReviews, medicalRecords);

            //Ensure all docs sent to Archer are in "in review"
            //Ensure all docs sent to Archer are tracked in PostgreSQL

            var incompleteRecordReviews = archerAudits.Where(a => a.RecordReviewStatusId != 3 && a.RecordReviewStatusId != 4);
            var completedRecordReviews = archerAudits.Where(x => x.RecordReviewStatusId == 3 || x.RecordReviewStatusId == 4);

            List<IncompleteAuditReport> incompleteAudits = new List<IncompleteAuditReport>();
            List<CompleteAuditReport> completeAuditReports = new List<CompleteAuditReport>();

            var count = 0;
            var totalCount = incompleteRecordReviews.Count();
            _logger.LogInformation("Auditing Incomplete Reviews");

            foreach (var incompleteRecordReview in incompleteRecordReviews)
            {
                _logger.LogInformation($"Auditing Incomplete Reviews {count} of {totalCount}");
                count++;
                var incompleteAudit = await AuditIncompleteReview(incompleteRecordReview);
                if (incompleteAudit != null)
                {
                    incompleteAudits.Add(incompleteAudit);
                }
            }
            _logger.LogInformation("Auditing Complete Reviews");
            count = 0;
            totalCount = completedRecordReviews.Count();
            foreach (var completedRecordReview in completedRecordReviews)
            {
                //Ensure all completed reviews have corresponding BMHL files
                //Ensure all completed reviews have corresponding docs in status "review complete"

                _logger.LogInformation($"Auditing Complete Reviews {count} of {totalCount}");
                count++;

                //if (completedRecordReview.RecordReview.CaseName != "Zantac Pharmaceutical Use")
                //{
                //    var completeAudit = await AuditCompleteReview(completedRecordReview);
                //    if (completeAudit != null)
                //    {
                //        completeAuditReports.Add(completeAudit);
                //    }
                //}

            }

            await WriteReport(incompleteAudits, completeAuditReports);
        }

        private List<MedicalRecord> ParseCSVForMedialRecords()
        {
            // Configure the CSV reader
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                // Don't throw an exception if a CSV line has fewer fields than the header record
                MissingFieldFound = null
            };
            using (var reader = new StreamReader("sigmaReport1213.csv"))
            using (var csv = new CsvReader(reader, config))
            {
                // Get the records and map them to your custom object
                var records = csv.GetRecords<MedicalRecord>();
                return records.ToList();
            }
        }

        private async Task<List<RecordReview>> GetRecordReviewsFromPostgres() 
        {
            _logger.LogInformation("Connecting To DB");
            var connectionString = DBConnectionString;
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var command = new NpgsqlCommand("SELECT * FROM \"RecordReviews\" WHERE \"ArcherId\" IS NOT NULL ", connection);
            using (var reader = command.ExecuteReader())
            {
                List<RecordReview> recordReviews = new List<RecordReview>();
                _logger.LogInformation("Reading Data From DB");
                var count = 0;
                while (reader.Read())
                {
                    _logger.LogInformation($"Reading Record Review {count}");
                    count++;
                    recordReviews.Add(new RecordReview
                    {
                        Id = reader.GetGuid(0),
                        MatterId = reader.GetString(1),
                        InjuredPartyId = reader.GetString(2),
                        StatusId = reader.GetInt32(4),
                        Updated = reader.GetDateTime(6),
                        SalesforceId = reader.GetString(7),
                        ArcherId = reader.GetInt32(8),
                        CaseName = reader.GetString(9),
                        Created = reader.GetDateTime(10),
                        INjuredPartyname = reader.GetString(11),
                        MedicalRecordIds = reader.IsDBNull(13)? null :  reader.GetString(13),
                    });
                }
                _logger.LogInformation("Data Read From DB");

                await reader.CloseAsync();
                await connection.CloseAsync();

                return recordReviews;
            }
        }

        private List<ArcherAudit> MapArcherAudits(List<RecordReview> recordReviews, List<MedicalRecord> medicalRecords)
        {
            var archerAudits = new List<ArcherAudit>();
            var count = 0;
            var totalCount = recordReviews.Count;
            foreach(var recordReview in recordReviews)
            {
                _logger.LogInformation($"Mapping Archer Audit {count} of {totalCount}");
                count++;

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

            var trackedMedicalRecords = completedRecordReview.MedicalRecordIdsFromPostgres.Count;
            //Get BookmarkHighlightedFiles

            var bmhlFiles = completedRecordReview.MedicalRecords.Where(x => x.FileName.Contains("BMHL")).Select(x => x.FileInfoId).ToList();

            var completeAudit = new CompleteAuditReport
            {
                MatterId = completedRecordReview.MatterId,
                NumberOfMedicalRecords = completedRecordReview.MedicalRecords.Count,
                NumberOfBMHLFiles = bmhlFiles.Count,
                MedicalRecordIds = completedRecordReview.MedicalRecords.Select(x=> x.MedicalReviewId).ToList(),
                BookmarkHighlightedFiles = bmhlFiles,
                BMHLFilesExist = bmhlFiles.Any(),
                NumberOfRecordsTrackedByPostgres = trackedMedicalRecords,
                CaseName = completedRecordReview.RecordReview.CaseName
            };

            return completeAudit;
        }

        private async Task<IncompleteAuditReport?> AuditIncompleteReview(ArcherAudit incompleteRecordReview)
        {

            //Check if all files are in review

            var validMedicalRecords = incompleteRecordReview.MedicalRecords.Where(x => incompleteRecordReview?.RecordReviewCreatedDate > x?.DocumentCreatedDate).ToList();

            var medicalRecordIdsNotInReview = validMedicalRecords.Where(x => x?.FileInfoStatus != "In Review").Select(x => x.FileInfoId).ToList();

            //Check if all files are tracked in PostgreSQL

            var medicalRecordIdsNotInPostgres = incompleteRecordReview.MedicalRecordIdsFromSigma.Except(incompleteRecordReview.MedicalRecordIdsFromPostgres).ToList();


            if (!medicalRecordIdsNotInReview.Any() && !medicalRecordIdsNotInPostgres.Any())
            {
                return null;
            }

            var incompleteAudit = new IncompleteAuditReport
            {
                MatterId = incompleteRecordReview.MatterId,
                MedicalRecordIdsMissingInPostgres = medicalRecordIdsNotInPostgres,
                MedicalRecordIdsUpdatedToCorrectStatus = medicalRecordIdsNotInReview,
                MedicalRecordsAreMissingInPostgres = medicalRecordIdsNotInPostgres.Any(),
                DocrioMedRecordsNotInCorrectStatus = medicalRecordIdsNotInReview.Any(),
                MedicalReviewCreatedDate = incompleteRecordReview.RecordReviewCreatedDate,
                MissingMedicalRecordsInPostgresCount = medicalRecordIdsNotInPostgres.Count,
                TotalMedicalRecordsInPostgres = incompleteRecordReview.MedicalRecordIdsFromPostgres.Count,
                WrongStatusMedicalRecordsCount = medicalRecordIdsNotInReview.Count,
                CaseName = incompleteRecordReview.RecordReview.CaseName
            };

            if (medicalRecordIdsNotInReview.Any())
            {
                ////Update Salesforce
                foreach (var medicalRecordId in medicalRecordIdsNotInReview)
                {
                    //await UpdateFileInfo()
                    try
                    {
                        await UpdateFileInfo(medicalRecordId);

                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }

            if (medicalRecordIdsNotInPostgres.Any())
            {
                ////Update Postgres
                incompleteRecordReview.RecordReview.AppendMedicalRecordIds(medicalRecordIdsNotInPostgres);
                incompleteRecordReview.RecordReview.RemoveDuplicateMedicalRecordIds();
                await UpdatePostgres(incompleteRecordReview.RecordReview);
            }

            return incompleteAudit;
        }

        private async Task UpdateFileInfo(string medicalRecordId)
        {
            var bearerToken = "00D4W0000090Y1x!AQEAQAINlLf_RiA7zwueA.Qo3Rx9arU1nZfDCaiavVQwiXGKzl9hm9JGPxETJGaGm.y.NW5y5Uj7JHoejfQM4lYGODqykg75";

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
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

        private async Task UpdatePostgres(RecordReview recordReview)
        {
            var connectionString = DBConnectionString;
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var query = $"UPDATE \"RecordReviews\" SET \"MedicalRecordIds\" = '{recordReview.MedicalRecordIds}' WHERE \"Id\" = '{recordReview.Id}'";
            using var command = new NpgsqlCommand(query, connection);
            await command.ExecuteNonQueryAsync();
            await connection.CloseAsync();
        }

        private async Task WriteReport(List<IncompleteAuditReport> incompleteAudits, List<CompleteAuditReport> completeAuditReports)
        {
            //Write to CSV

            try
            {
                await WriteIncompleteAuditReport(incompleteAudits);
                await WriteCompleteAuditReport(completeAuditReports);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

        }

        private async Task WriteIncompleteAuditReport(List<IncompleteAuditReport> incompleteAudits)
        {
            //Write to CSV
            var csv = new StringBuilder();


            csv.AppendLine("Matterid, Postgres Missing Documents, Medical Records Missing in Postgres, Medical Records Missing in Postgres Count, Total Medical Records in Postgres, Medical Records Are set to Incorrect Status, Medical Records Updated to Correct Status,  Wrong Status Medical Records Count, Medical Review Created Date, Case Name");

            foreach(var audit in incompleteAudits)
            {
                csv.AppendLine($"{audit.MatterId}, {audit.MedicalRecordsAreMissingInPostgres}, { string.Join("|", audit.MedicalRecordIdsMissingInPostgres)}, {audit.MissingMedicalRecordsInPostgresCount}, {audit.TotalMedicalRecordsInPostgres},  {audit.MedicalRecordIdsUpdatedToCorrectStatus}, { string.Join("|", audit.MedicalRecordIdsUpdatedToCorrectStatus)}, {audit.WrongStatusMedicalRecordsCount}, {audit.MedicalReviewCreatedDate}, {audit.CaseName} ");
            }
            await File.WriteAllTextAsync("IncompleteAuditReport.csv", csv.ToString());
            Console.WriteLine("Incomplete Reviews Audit has been created.");

        }

        private async Task WriteCompleteAuditReport(List<CompleteAuditReport> completeAuditReports)
        {
            //Write to CSV
            var csv = new StringBuilder();
            csv.AppendLine("Matter Id,Number Of Medical Records,Number Of BMHL Files,Medical Record Ids,Bookmark Highlighted Files,BMHL Files Exist,Number of Med Ids tracked by Postgres, Case Name");
            foreach (var audit in completeAuditReports)
            {
                var line = $"{audit.MatterId},{audit.NumberOfMedicalRecords},{audit.NumberOfBMHLFiles},{string.Join("|", audit.MedicalRecordIds)},{string.Join("|", audit.BookmarkHighlightedFiles)},{audit.BMHLFilesExist}, {audit.NumberOfRecordsTrackedByPostgres}, {audit.CaseName}";
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
