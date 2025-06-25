using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Laboratory.ArcherClient
{
    public class ArcherClientOptions
    {
        public string BaseAddress { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
        public bool EnableHealthCheck { get; set; } = false;
    }

}
