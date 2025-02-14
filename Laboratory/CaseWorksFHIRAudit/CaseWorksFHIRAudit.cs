using System.Globalization;
using CsvHelper.Configuration;
using CsvHelper;

namespace Laboratory.CaseWorksFHIRAudit;

public class CaseWorksFHIRAudit
{
       public void AuditCaseWorkFHIRFiles()
    {
        var caseWorksRequests = GetMatterIds();
        var caseworksFiles = ParseCSVForCaseWorksFileInfo();
        var listOfAuditReports = new List<CaseWorksAuditReport>();
        var totalMatters = caseWorksRequests.Count;
        var matterCount = 0;
        var missingFHIRCount = 0;
        foreach (var caseWorksRequest in caseWorksRequests)
        {
            matterCount++;
            double percentComplete = (matterCount / totalMatters) * 100;
            Console.WriteLine($"{percentComplete}% complete");
            var filesForMatter = caseworksFiles.Where(x => x.MatterId == caseWorksRequest.MatterId).ToList();
            if (filesForMatter.Count == 0)
            {
                //Console.WriteLine($"No files found for matterId: {matterId}");
                continue;
            }

            var totalFiles = filesForMatter.Count;
            var FHIRpresent = filesForMatter.Any(x => x.FileName.ToLower().Contains("fhir") || x.FileName.ToLower().Contains("json"));

            if (!FHIRpresent)
            {
                missingFHIRCount++;
                Console.WriteLine($"No FHIR files found for matterId: {caseWorksRequest.MatterId}");
                var auditReport = new CaseWorksAuditReport
                {
                    MatterId = caseWorksRequest.MatterId,
                    SubjectOfRequest = filesForMatter.First().SubjectOfRequest,
                    Id = caseWorksRequest.Id,
                    VendorRequestId = caseWorksRequest.VendorRequestId,
                    CreatedDate = caseWorksRequest.CreatedDate
                };
                listOfAuditReports.Add(auditReport);
            }
        }

        using (var writer = new StreamWriter("CaseWorksFHIRAuditReport.csv"))
        {
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteRecords(listOfAuditReports);
            }
        }
    }


    private List<CaseWorksRequestInfo> GetMatterIds()
    {
        // Configure the CSV reader
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Don't throw an exception if a CSV line has fewer fields than the header record
            MissingFieldFound = null
        };
        using (var reader = new StreamReader("CaseworksRequests.csv"))
        using (var csv = new CsvReader(reader, config))
        {
            // Get the records and map them to your custom object
            var records = csv.GetRecords<CaseWorksRequestInfo>();
            return records.ToList();
        }
    }

    private List<CaseWorksFileInfo> ParseCSVForCaseWorksFileInfo()
    {
        // Configure the CSV reader
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Don't throw an exception if a CSV line has fewer fields than the header record
            MissingFieldFound = null
        };
        using (var reader = new StreamReader("CaseWorksFHIRAuditReport.csv"))
        using (var csv = new CsvReader(reader, config))
        {
            // Get the records and map them to your custom object
            var records = csv.GetRecords<CaseWorksFileInfo>();
            return records.ToList();
        }
    }
}

public class CaseWorksAuditReport
{
    public string MatterId {get; set;}
    public string SubjectOfRequest {get; set;}
    public string Id {get; set;}
    public string VendorRequestId {get; set;}
    public DateTime CreatedDate {get; set;}
}

public class CaseWorksFileInfo
{
    public string MatterId { get; set; }
    public string FileContent { get; set; }
    public string FileName { get; set; }
    public DateTime CreatedDate { get; set; }
    public string FileInfoId {get; set; }
    public string SubjectOfRequest {get; set;}
}

public class CaseWorksRequestInfo
{
    public string MatterId {get; set;}
    public string VendorRequestId {get; set;}
    public string Id {get; set;}
    public DateTime CreatedDate {get; set;}
}