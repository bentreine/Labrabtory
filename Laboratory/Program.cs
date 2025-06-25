// See https://aka.ms/new-console-template for more information
using KellerPostman.MedicalRecords.Infrastructure.BoxWrapper;
using KellerPostman.Salesforce.Sdk;
using Laboratory;
using Laboratory.ArcherClient;
using Laboratory.CaseWorksFHIRAudit;
using Laboratory.SalesforceClient;
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
        services.AddScoped<DataVantScriptWriter>();
        services.AddScoped<SettLitAudit>();
        services.AddScoped<ILocalSalesforceClient, LocalSalesforceClient>();
        services.AddScoped<IArcherClient, ArcherClient>();


        IConfiguration configuration = context.Configuration;


        services.AddSalesforceSdk(options =>
        {
            var appSettings = context.Configuration.GetSection("AppSettings");
            options.BaseUri = appSettings["SalesforceUri"];
            options.ClientId = appSettings["SalesforceClientId"];
            options.ClientSecret = appSettings["SalesforceClientSecret"];
            options.BaseAddress = appSettings["SalesforceUri"];
        });
    })
    .Build();

host.Start();

var reuploadService = host.Services.GetRequiredService<ReuploadBoxFiles>();



await reuploadService.ManualStartReview("a0LNw000003yfLBMAY");

