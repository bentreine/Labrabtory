// See https://aka.ms/new-console-template for more information
using Laboratory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;



var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("appsettings.json", optional: false);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddScoped<RagicUtility>();
        services.AddScoped<RecordReviewMetatdataUtility>();
        services.AddHttpClient();
        services.Configure<LaboratoryOptions>(context.Configuration.GetSection("AppSettings"));

    })
    .Build();

host.Start();

var service = host.Services.GetRequiredService<RagicUtility>();
var metaService = host.Services.GetRequiredService<RecordReviewMetatdataUtility>();

//await service.AuditCompletedMedicalReviews();

await metaService.AuditArcher();

//await service.UpdateClientInformationOnRagic();