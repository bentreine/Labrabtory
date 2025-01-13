using System.Globalization;
using CsvHelper;
using Microsoft.Extensions.Logging;
using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

public class PdfMerger
{
    private readonly ILogger<PdfMerger> _logger;

    public PdfMerger(ILogger<PdfMerger> logger)
    {
        _logger = logger;
    }

    public void StichPdfs()
    {
        var folder = @"C:\Users\BenjaminTreine\source\repos\Laboratory\Laboratory\bin\Debug\PDFS";
        string[] files = Directory.GetFiles(folder, "*.pdf");

        List<string> pdfPrefixes = files.Select (fileName => fileName.Split('_')[0]).Distinct().ToList();

        _logger.LogInformation("Merging PDFs");
        foreach (var prefix in pdfPrefixes)
        {
            var pdfFiles = files.Where(fileName => fileName.Contains(prefix)).ToList();
            MergePdfs(pdfFiles, $"{folder}\\{prefix}", prefix);
            _logger.LogInformation($"Merged {pdfFiles.Count} PDFs with prefix {prefix}");
        }
    }

    public void MergePdfs(List<string> pdfFiles, string outputFileFolder, string outputFilePrefix)
    {
        PageSize size = PageSize.Letter;

        List<PDFMetaDataKP> csvData = new List<PDFMetaDataKP>();

        using (PdfDocument outputDocument = new PdfDocument())
        {
            int currentpage = 1;
            foreach (string pdfFile in pdfFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(pdfFile);
                var pageStart = currentpage;
                using (PdfDocument inputDocument = PdfReader.Open(pdfFile, PdfDocumentOpenMode.Import))
                {
                    for (int i = 0; i < inputDocument.PageCount; i++)
                    {
                        currentpage++;
                        PdfPage page = inputDocument.Pages[i];
                        page.Size = size;
                        outputDocument.AddPage(page);
                        //List csv logic
                    }
                }
                var pageEnd = currentpage;
                currentpage++;
                csvData.Add(new PDFMetaDataKP(fileName, pageStart.ToString(), pageEnd.ToString()));
            }
            //create Folder
            //Create CSV
            //Create PDF
        if (!Directory.Exists(outputFilePrefix))
        {
            Directory.CreateDirectory(outputFilePrefix);
            Console.WriteLine($"Folder created at: {outputFilePrefix}");
        }
        using (var writer = new StreamWriter($"{outputFilePrefix}\\TableOfContents.csv"))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(csvData);
            
        }
            outputDocument.Save($"{outputFilePrefix}\\StichedPdf.pdf");
        }
    }
}

public class PDFMetaDataKP
{
    public string FileName { get; set; }
    public string PageStart { get; set; }
    public string PageEnd { get; set; }

    public PDFMetaDataKP(string fileName, string pageStart, string pageEnd)
    {
        FileName = fileName;
        PageStart = pageStart;
        PageEnd = pageEnd;
    }
}