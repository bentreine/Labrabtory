
using System.Text.Json;

namespace Laboratory.Models
{
    public class ArcherAudit
    {
        public string MatterId  {get; set; }

        public string MedicalReviewId { get; set; }

        public RecordReview RecordReview { get; set; }

        public List<MedicalRecord> MedicalRecords { get; set; }

        public DateTime RecordReviewCreatedDate { get; set; }

        public int RecordReviewStatusId { get; set; }

        public List<string> MedicalRecordIdsFromSigma { get; private set; }

        public List<string> MedicalRecordIdsFromPostgres { get; private set }
        public ArcherAudit(RecordReview review, List<MedicalRecord> medicalRecords)
        {
            RecordReview = review;
            MedicalRecords = medicalRecords;
            MatterId = review.MatterId;
            MedicalReviewId = review.SalesforceId;
            RecordReviewCreatedDate = review.Created;
            RecordReviewStatusId = review.StatusId;
        }

        public void SetMedicalRecordIds()
        {
            MedicalRecordIdsFromSigma = MedicalRecords.Select(x => x.FileInfoId).ToList();

            if (!string.IsNullOrEmpty(RecordReview.MedicalRecordIds))
            {
                MedicalRecordIdsFromPostgres = JsonSerializer.Deserialize<List<string>>(RecordReview.MedicalRecordIds);
            }
        }
    }


    public class RecordReview
    {
        public Guid Id { get; set; }
        public string MatterId { get; set; }
        public string InjuredPartyId { get; set; }
        public string ReviewDetails { get; set; }
        //Have to map this correclty
        public int StatusId { get; set; }
        public string Error { get; set; }
        public DateTime Updated { get; set; }
        public string SalesforceId { get; set; }
        public int ArcherId { get; set; }
        public string CaseName { get; set; }
        public DateTime Created { get; set; }
        public string INjuredPartyname { get; set; } 
        public Guid DocumentReviewId { get; set; } 
        public string MedicalRecordIds { get; set; }


        public virtual void AppendMedicalRecordIds(List<string> recordIds)
        {
            if (string.IsNullOrEmpty(MedicalRecordIds))
            {
                SetMedicalRecordIds(recordIds);
            }
            else
            {
                var initialList = JsonSerializer.Deserialize<List<string>>(MedicalRecordIds) ?? new List<string>();
                initialList.AddRange(recordIds);
                MedicalRecordIds = JsonSerializer.Serialize(initialList);
            }
        }

        public virtual void SetMedicalRecordIds(List<string> recordIds)
        {
            var jsonString = JsonSerializer.Serialize(recordIds);
            MedicalRecordIds = jsonString;
        }
    }

    public class MedicalRecord
    {
        public string DocumentCategory { get; set; }
        public string FileContent { get; set; }
        public string FileName { get; set; }
        public string FolderPath { get; set; }
        public string CreatedBy { get; set; }
        public DateTime DocumentCreatedDate { get; set; }
        public string FileInfoId { get; set; }
        public string FileInfoStatus { get; set; }
        public string MatterId { get; set; }
        public string LastModifiedBy { get; set; }
        public string Owner { get; set; }
        public string CaseName { get; set; }
        public string CliamType { get; set; }   
        public string Client { get; set; }
        public string InjuredParty { get; set; }
        public string MedicalReviewId { get; set; }

        public DateTime MedicalReviewCreatedDate { get; set; }

    }
}
