namespace Laboratory.Models
{
    public class CompleteAuditReport
    {
        public string MatterId { get; set; }
        public int NumberOfMedicalRecords { get; set; }
        public int NumberOfBMHLFiles { get; set; }

        public List<string> MedicalRecordIds { get; set; }

        public List<string> BookmarkHighlightedFiles { get; set; }

        public bool BMHLFilesExist { get; set; }

        public int NumberOfRecordsTrackedByPostgres { get; set; }

        public string CaseName { get; set; }

    }
}
