using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

public class DataVantScriptWriter
{
    public void WriteScripts()
    {
        List<DatavantFacility> facilities = ParseCSV<DatavantFacility>("Facilities.csv");

        var scriptCSV = new StringBuilder();
        foreach (var facility in facilities)
        {
            var combinedAddress = string.Join(", ", facility.Address, facility.Address2).ToTileCase();
            var guid = Guid.NewGuid();
            string script = $@"
            INSERT INTO public.""Facilities""
           ([Id]
           ,[ExternalId]
           ,[Name]
           ,[Adress]
           ,[City]
           ,[State]
           ,[Zip]
           ,[Phone]
           ,[Fax]
           ,[Source]
           ,[Datavant_Address1]
           ,[Datavant_Address2]
           ) 
            VALUES
           ('{guid}' 
           ,'{guid}'
           ,'{facility.SiteName.ToTileCase()}'
           ,'{combinedAddress.ToTileCase()}'
           ,'{facility.City}'
           ,'{facility.State}'
           ,'{facility.Zip}'
           ,'{facility.Phone}'
           ,'{facility.Fax}'
           ,'Datavant'
           ,'{facility.Address}'
           ,'{facility.Address2}'
           );";

            scriptCSV.AppendLine(script);
        }

        File.WriteAllText("FacilityScripts.sql", scriptCSV.ToString());
    }

    private List<T> ParseCSV<T>(string csvFilePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Don't throw an exception if a CSV line has fewer fields than the header record
            MissingFieldFound = null
        };
        using (var reader = new StreamReader(csvFilePath))
        using (var csv = new CsvReader(reader, config))
        {
            // Get the records and map them to your custom object
            var records = csv.GetRecords<T>();
            return records.ToList();
        }
    }

    public class DatavantFacility
    {
        public string? SiteName { get; set; }
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? Phone {get; set;}
        public string? Fax {get; set;}

    }
}