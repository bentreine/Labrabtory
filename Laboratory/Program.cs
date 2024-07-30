// See https://aka.ms/new-console-template for more information
using Labrabtory;
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
        services.AddHttpClient();
        services.Configure<LabratoryOptions>(context.Configuration.GetSection("AppSettings"));

    })
    .Build();

host.Start();

var service = host.Services.GetRequiredService<RagicUtility>();
await service.UpdateClientInformationOnRagic();