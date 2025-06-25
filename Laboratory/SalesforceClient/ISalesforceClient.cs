using KellerPostman.Salesforce.Sdk.Salesforce;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Laboratory.SalesforceClient
{
    public interface ILocalSalesforceClient
    {
        public Task UpdateDocumentFileInfo(string medicalRecordId, FileInfoStatus updateStatus);

    }
}
