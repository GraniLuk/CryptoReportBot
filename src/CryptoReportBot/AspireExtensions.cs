using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http.Resilience;

namespace CryptoReportBot
{
    public static class AspireExtensions
    {
        /// <summary>
        /// Adds .NET Aspire service defaults to the application
        /// </summary>
        public static IHostBuilder AddAspireServiceDefaults(this IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                // Add health checks
                services.AddHealthChecks()
                    .AddCheck("self", () => HealthCheckResult.Healthy(), new[] { "service" });

                // Configure resilience and retry policies
                services.AddHttpClientWithResilienceDefaults();

                // Add OpenTelemetry for telemetry collection
                services.AddOpenTelemetry();
            });
        }

        /// <summary>
        /// Configures HTTP clients with resilience policies
        /// </summary>
        private static IServiceCollection AddHttpClientWithResilienceDefaults(this IServiceCollection services)
        {
            // Configure default resilience handler for HttpClient
            services.ConfigureHttpClientDefaults(builder =>
            {
                // Add standard resilience policy with exponential backoff
                builder.AddStandardResilienceHandler();
            });

            return services;
        }

        /// <summary>
        /// Adds OpenTelemetry for telemetry collection
        /// </summary>
        private static IServiceCollection AddOpenTelemetry(this IServiceCollection services)
        {
            // This is a simplified implementation - in a full Aspire setup, this would be more detailed
            services.AddLogging(builder =>
            {
                builder.AddConsole();
            });

            return services;
        }
    }
}