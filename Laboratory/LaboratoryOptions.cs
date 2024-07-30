using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Laboratory;

public class LaboratoryOptions
{
    public string? PostgresConnectionString { get; set; }
    public string? SalesforceUri { get; set; }
    public string? SalesforceClientId { get; set; }
    public string? SalesforceClientSecret { get; set; }
    public string? ArcherUri { get; set; }
    public string? ArcherApiKey { get; set; }
}
