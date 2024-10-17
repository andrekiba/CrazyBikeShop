using System;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace CrazyBikeShop.OrderProcessor;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        await host.RunAsync();
    }

    static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureLogging((hostBuilderContext, loggingBuilder) =>
            {
                var logger = new LoggerConfiguration()
                    .WriteTo.Console(
                        outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}",
                        theme: AnsiConsoleTheme.Code)
                    .MinimumLevel.Error()
                    .CreateLogger();

                loggingBuilder.AddSerilog(logger);
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddAzureClients(builder =>
                {
                    builder.AddTableServiceClient(new Uri(hostContext.Configuration["Storage:Tables"]!))
                        .WithName("tables");
            
                    builder.UseCredential(new DefaultAzureCredential());
                    builder.ConfigureDefaults(hostContext.Configuration.GetSection("AzureDefaults"));
                });
                services.AddHostedService<Worker>();
            });
    }
}