using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoReportBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Set up global unhandled exception handler
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                Console.Error.WriteLine($"CRITICAL ERROR: Unhandled exception: {eventArgs.ExceptionObject}");
                // Force log to be flushed
                System.Diagnostics.Trace.Flush();
            };

            // Set up task exception handler
            TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
            {
                Console.Error.WriteLine($"CRITICAL ERROR: Unobserved task exception: {eventArgs.Exception}");
                eventArgs.SetObserved(); // Prevent the process from terminating
                System.Diagnostics.Trace.Flush();
            };
            
            using IHost host = CreateHostBuilder(args).Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Application starting at: {Time}", DateTimeOffset.UtcNow);

            try
            {
                // Validate configuration early
                logger.LogInformation("Validating configuration...");
                var config = host.Services.GetRequiredService<IConfigurationManager>();
                
                // Trigger lazy loading and validation by accessing properties
                try
                {
                    var botToken = config.BotToken;
                    var azureUrl = config.AzureFunctionUrl;
                    logger.LogInformation("Configuration validation successful");
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogCritical("Configuration validation failed: {Error}", ex.Message);
                    logger.LogError("Please ensure the following environment variables are set:");
                    logger.LogError("- alerts_bot_token: Your Telegram bot token");
                    logger.LogError("- azure_function_url: URL of your Azure Function");
                    logger.LogError("- azure_function_key: (Optional) Azure Function access key");
                    logger.LogError("- allowed_user_ids: (Optional) Comma-separated list of allowed Telegram user IDs");
                    throw;
                }
                
                // Force resolve any bot conflicts before starting
                logger.LogInformation("Checking for bot conflicts and resolving them...");
                var httpClient = host.Services.GetRequiredService<HttpClient>();
                var conflictResolver = new TelegramBotConflictResolver(
                    config.BotToken, 
                    host.Services.GetRequiredService<ILogger<TelegramBotConflictResolver>>(),
                    httpClient);
                
                // Get diagnostics first
                var diagnostics = await conflictResolver.GetConflictDiagnosticsAsync();
                logger.LogInformation("Bot conflict diagnostics:\n{Diagnostics}", diagnostics);
                
                // Force resolve conflicts
                var conflictResolved = await conflictResolver.ForceResolveConflictsAsync();
                if (!conflictResolved)
                {
                    logger.LogWarning("Could not fully resolve bot conflicts, but continuing anyway...");
                }
                
                // Retrieve Bot instance and start it
                var bot = host.Services.GetRequiredService<Bot>();
                logger.LogInformation("Bot service resolved successfully");
                
                await bot.StartAsync();
                logger.LogInformation("Bot started successfully");
                
                // Start periodic health check
                var healthCheckTimer = new Timer(
                    _ => PerformHealthCheck(host.Services), 
                    null, 
                    TimeSpan.Zero, 
                    TimeSpan.FromMinutes(5));
                
                await host.RunAsync();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Bot token") || ex.Message.Contains("Azure Function"))
            {
                logger.LogCritical(ex, "Configuration error - missing required settings");
                logger.LogError("Please ensure the following environment variables are set:");
                logger.LogError("- alerts_bot_token: Your Telegram bot token");
                logger.LogError("- azure_function_url: URL of your Azure Function");
                logger.LogError("- azure_function_key: (Optional) Azure Function access key");
                logger.LogError("- allowed_user_ids: (Optional) Comma-separated list of allowed Telegram user IDs");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Application terminated unexpectedly");
                
                // Additional diagnostics for dependency injection failures
                if (ex.Message.Contains("dependency injection") || ex.GetType().Name.Contains("ServiceProvider"))
                {
                    logger.LogError("This appears to be a dependency injection error. Check that all services are properly registered.");
                }
                
                throw;
            }
            finally
            {
                logger.LogInformation("Application shutting down at: {Time}", DateTimeOffset.UtcNow);
            }
        }

        private static void PerformHealthCheck(IServiceProvider services)
        {
            try
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                var memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024); // MB
                var threadCount = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
                
                logger.LogInformation(
                    "Health check - Memory: {MemoryMB} MB, Threads: {ThreadCount}, Time: {Time}", 
                    memoryUsage, 
                    threadCount, 
                    DateTimeOffset.UtcNow);

                // Force GC collection to prevent memory leaks
                if (memoryUsage > 200) // If memory usage exceeds 200 MB
                {
                    logger.LogWarning("High memory usage detected ({MemoryMB} MB). Running garbage collection.", memoryUsage);
                    GC.Collect();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error in health check: {ex}");
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls("http://+:80");
                    webBuilder.Configure(app =>
                    {
                        // Add health check endpoint
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
                            {
                                ResultStatusCodes =
                                {
                                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                                }
                            });
                            
                            // Add a simple root endpoint
                            endpoints.MapGet("/", async context =>
                            {
                                await context.Response.WriteAsync("CryptoReportBot is running");
                            });
                        });
                    });
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options => 
                    {
                        // Simple configuration that doesn't use formatter options
                    });
                    
                    // Set minimum log level based on environment
                    if (context.HostingEnvironment.IsProduction())
                    {
                        logging.SetMinimumLevel(LogLevel.Information);
                    }
                    else
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    }
                    
                    logging.AddFilter("System.Net.Http", LogLevel.Information);
                })
                .ConfigureServices((context, services) =>
                {
                    // Add health checks
                    services.AddHealthChecks()
                        .AddCheck("self", () => HealthCheckResult.Healthy("Bot is running"));
                    
                    // Add Application Insights
                    services.AddApplicationInsightsTelemetry();
                    
                    // Register configuration (simplified - only uses environment variables)
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