using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.IO;
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
            
            // Add detailed environment diagnostics for Docker investigation
            Console.WriteLine("=== ENVIRONMENT DIAGNOSTICS ===");
            Console.WriteLine($"Process ID: {Environment.ProcessId}");
            Console.WriteLine($"Machine Name: {Environment.MachineName}");
            Console.WriteLine($"OS Version: {Environment.OSVersion}");
            Console.WriteLine($"Working Directory: {Environment.CurrentDirectory}");
            Console.WriteLine($"Command Line: {Environment.CommandLine}");
            Console.WriteLine($"Is Docker Container: {File.Exists("/.dockerenv")}");
            Console.WriteLine($"Temp Path: {Path.GetTempPath()}");
            Console.WriteLine($"Environment Variables:");
            Console.WriteLine($"  ASPNETCORE_ENVIRONMENT: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}");
            Console.WriteLine($"  WEBHOOK_MODE: {Environment.GetEnvironmentVariable("WEBHOOK_MODE")}");
            Console.WriteLine($"  alerts_bot_token exists: {!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("alerts_bot_token"))}");
            Console.WriteLine("=== END DIAGNOSTICS ===");
            
            using IHost host = CreateHostBuilder(args).Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Application starting at: {Time}", DateTimeOffset.UtcNow);

            try
            {
                // Start the web host immediately so Azure can health check it
                var hostTask = host.RunAsync();
                
                // Run bot initialization in background task
                var botInitTask = Task.Run(async () =>
                {
                    try
                    {
                        await InitializeBotAsync(host.Services, logger);
                    }
                    catch (Exception ex)
                    {
                        logger.LogCritical(ex, "Critical error during bot initialization");
                        Environment.Exit(1);
                    }
                });
                
                // Wait for either the host to stop or bot init to complete
                await Task.WhenAny(hostTask, botInitTask);
                
                // If bot init completed, wait for host
                if (botInitTask.IsCompleted)
                {
                    await hostTask;
                }
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

        private static async Task InitializeBotAsync(IServiceProvider services, ILogger<Program> logger)
        {
            // Validate configuration early
            logger.LogInformation("Validating configuration...");
            var config = services.GetRequiredService<IConfigurationManager>();
            
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
            
            // Ensure only one bot instance runs at a time using singleton lock
            logger.LogInformation("Acquiring singleton lock to prevent multiple bot instances...");
            using var singletonManager = new SingletonBotManager(services.GetRequiredService<ILogger<SingletonBotManager>>());
            
            // First try immediate lock
            var lockAcquired = await singletonManager.TryAcquireLockAsync();
            if (!lockAcquired)
            {
                logger.LogWarning("Another bot instance detected. Waiting for it to stop...");
                // Wait up to 30 seconds for other instance to stop
                lockAcquired = await singletonManager.WaitAndAcquireLockAsync(TimeSpan.FromSeconds(30));
                
                if (!lockAcquired)
                {
                    logger.LogError("Could not acquire singleton lock. Another instance may be running or there's a stale lock file.");
                    logger.LogInformation("You can manually delete the lock file if you're sure no other instance is running.");
                    return; // Exit gracefully instead of throwing
                }
            }
            
            logger.LogInformation("✅ Singleton lock acquired. This is the only bot instance running.");
            
            // Force resolve any bot conflicts before starting
            logger.LogInformation("Checking for bot conflicts and resolving them...");
            var httpClient = services.GetRequiredService<HttpClient>();
            var conflictResolver = new TelegramBotConflictResolver(
                config.BotToken, 
                services.GetRequiredService<ILogger<TelegramBotConflictResolver>>(),
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
            var bot = services.GetRequiredService<Bot>();
            logger.LogInformation("Bot service resolved successfully");
            
            // Check if webhook mode is requested (useful to avoid polling conflicts)
            var webhookMode = Environment.GetEnvironmentVariable("WEBHOOK_MODE")?.ToLowerInvariant() == "true";
            var webhookUrl = Environment.GetEnvironmentVariable("WEBHOOK_URL");
            
            if (webhookMode && !string.IsNullOrEmpty(webhookUrl))
            {
                logger.LogInformation("Starting bot in webhook mode with URL: {WebhookUrl}", webhookUrl);
                await bot.StartWithWebhookAsync(webhookUrl);
                logger.LogInformation("Bot webhook configured successfully");
            }
            else
            {
                logger.LogInformation("Starting bot in polling mode");
                await bot.StartAsync();
                logger.LogInformation("Bot started successfully");
            }
            
            // Start periodic health check
            var healthCheckTimer = new Timer(
                _ => PerformHealthCheck(services), 
                null, 
                TimeSpan.Zero, 
                TimeSpan.FromMinutes(5));
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
                    // For Azure Web Apps, bind to all interfaces on port 80
                    // Use ASPNETCORE_URLS environment variable if set, otherwise default to port 80
                    var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
                    if (!string.IsNullOrEmpty(urls))
                    {
                        webBuilder.UseUrls(urls);
                    }
                    else
                    {
                        // Default to port 80 for Azure Web Apps
                        webBuilder.UseUrls("http://+:80");
                    }
                    
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
                            
                            // Add webhook endpoint for Telegram
                            endpoints.MapPost("/webhook", async context =>
                            {
                                try
                                {
                                    var bot = context.RequestServices.GetRequiredService<Bot>();
                                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                                    
                                    using var reader = new System.IO.StreamReader(context.Request.Body);
                                    var json = await reader.ReadToEndAsync();
                                    
                                    if (!string.IsNullOrEmpty(json))
                                    {
                                        var update = System.Text.Json.JsonSerializer.Deserialize<Telegram.Bot.Types.Update>(json);
                                        if (update != null)
                                        {
                                            await bot.HandleWebhookUpdateAsync(update);
                                            context.Response.StatusCode = 200;
                                            await context.Response.WriteAsync("OK");
                                            return;
                                        }
                                    }
                                    
                                    logger.LogWarning("Received empty or invalid webhook payload");
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsync("Bad Request");
                                }
                                catch (Exception ex)
                                {
                                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                                    logger.LogError(ex, "Error processing webhook");
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync("Internal Server Error");
                                }
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