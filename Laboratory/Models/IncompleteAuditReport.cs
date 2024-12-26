using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Laboratory.Models
{
    public class IncompleteAuditReport
    {
        public string MatterId { get; set; }
        public List<string> MedicalRecordIdsMissingInPostgres { get; set; }

        public List<string> MedicalRecordIdsUpdatedToCorrectStatus { get; set; }

        public bool MedicalRecordsAreMissingInPostgres { get; set; }

        public bool DocrioMedRecordsNotInCorrectStatus { get; set; }

        public int MissingMedicalRecordsInPostgresCount { get; set; }

        public int WrongStatusMedicalRecordsCount { get; set; }

        public int TotalMedicalRecordsInPostgres { get; set; }

        public DateTime MedicalReviewCreatedDate { get; set; }

        public string CaseName { get; set; }
    }
}
