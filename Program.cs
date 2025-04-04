using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace CryptoReportBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            using IHost host = CreateHostBuilder(args).Build();
            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostContext, config) =>
                {
                    // Add User Secrets in development environment
                    if (hostContext.HostingEnvironment.IsDevelopment())
                    {
                        config.AddUserSecrets<Program>();
                    }
                    
                    // Add local.settings.json if it exists (for local development)
                    config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
                })
                .ConfigureLogging(logging =>
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
