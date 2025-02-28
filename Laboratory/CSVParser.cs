using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

public class CSVParser
{
    public List<T> ParseCSV<T>(string csvFilePath)
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
}