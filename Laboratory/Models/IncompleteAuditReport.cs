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
    }
}
