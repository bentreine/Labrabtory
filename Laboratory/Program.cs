// See https://aka.ms/new-console-template for more information
using KellerPostman.MedicalRecords.Infrastructure.BoxWrapper;
using Laboratory;
using Laboratory.CaseWorksFHIRAudit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Org.BouncyCastle.Security;
using System.IO.Abstractions;



var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("appsettings.json", optional: false);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddScoped<RagicUtility>();
        services.AddScoped<RecordReviewMetatdataUtility>();
        services.AddScoped<WeeklyArcherAudit>();
        services.AddHttpClient();
        services.Configure<LaboratoryOptions>(context.Configuration.GetSection("AppSettings"));

        services.AddScoped<IBoxClient, BoxClient>();
        services.AddScoped<IDocumentStoreClient, DocumentStoreClient>();
        services.AddScoped<IDocrioClient, DocrioClient>();
        services.AddTransient<IFileSystem, FileSystem>();
        services.AddScoped<ReuploadBoxFiles>();
        services.AddScoped<PdfMerger>();
        services.AddScoped<CaseWorksFHIRAudit>();
    })
    .Build();

host.Start();

//var service = host.Services.GetRequiredService<RagicUtility>();
//var metaService = host.Services.GetRequiredService<RecordReviewMetatdataUtility>();
//var weeklyAuditService = host.Services.GetRequiredService<WeeklyArcherAudit>();
//var reuploadService = host.Services.GetRequiredService<ReuploadBoxFiles>();
var pdfMerger = host.Services.GetRequiredService<PdfMerger>();
var CaseWorksAuditReport = host.Services.GetRequiredService<CaseWorksFHIRAudit>();

//Select Job To Run

//pdfMerger.StichPdfs();

//await reuploadService.ReuploadFiles();

//await weeklyAuditService.RunWeeklyArcherAudit();

//await service.AuditCompletedMedicalReviews();

//await metaService.AuditArcher();

//await service.UpdateClientInformationOnRagic();

CaseWorksAuditReport.AuditCaseWorkFHIRFiles();