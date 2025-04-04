using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace CryptoReportBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Setup Host
            using IHost host = CreateHostBuilder(args).Build();
            
            // Get bot service and start it
            var bot = host.Services.GetRequiredService<Bot>();
            await bot.StartAsync();
            
            // Keep the application running
            await host.RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                })
                .ConfigureServices((context, services) =>
                {
                    // Register configuration
                    services.AddSingleton<IConfigurationManager, ConfigurationManager>();
                    
                    // Register core services
                    services.AddHttpClient(); 
                    services.AddSingleton<IAzureFunctionsClient, AzureFunctionsClient>();
                    
                    // Register conversation handlers
                    services.AddSingleton<CreateAlertHandler>();
                    services.AddSingleton<CreateGmtAlertHandler>();
                    services.AddSingleton<RemoveAlertHandler>();
                    services.AddSingleton<ListAlertsHandler>();
                    
                    // Register the main bot
                    services.AddSingleton<Bot>();
                });
    }
}
