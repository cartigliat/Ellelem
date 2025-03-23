using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using ollamidesk.Configuration;

namespace ollamidesk.Services
{
    /// <summary>
    /// Configures HTTP clients for the application
    /// </summary>
    public static class HttpClientConfiguration
    {
        /// <summary>
        /// Configures HTTP clients with the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="appSettings">The application settings</param>
        public static void ConfigureHttpClients(IServiceCollection services, AppSettings appSettings)
        {
            // Configure the Ollama API client
            services.AddHttpClient("OllamaApi", client =>
            {
                client.BaseAddress = new Uri(appSettings.Ollama.ApiBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(appSettings.Ollama.TimeoutSeconds);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("User-Agent", "OllamaDesk/1.0");

                // Configure connection reuse
                client.DefaultRequestHeaders.ConnectionClose = false; // Keep connection alive
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Connection pooling and reuse configuration
                PooledConnectionLifetime = TimeSpan.FromMinutes(10), // Keep connections alive for 10 minutes
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5), // Close idle connections after 5 minutes
                MaxConnectionsPerServer = 20, // Allow up to 20 concurrent connections per server
                EnableMultipleHttp2Connections = true, // Enable multiple HTTP/2 connections
                AutomaticDecompression = DecompressionMethods.All, // Automatically handle compressed responses
            })
            .AddPolicyHandler(GetRetryPolicy(appSettings.Ollama.MaxRetries, appSettings.Ollama.RetryDelayMs))
            .AddPolicyHandler(GetCircuitBreakerPolicy());
        }

        /// <summary>
        /// Creates a retry policy for HTTP requests
        /// </summary>
        /// <param name="maxRetries">Maximum number of retries</param>
        /// <param name="retryDelayMs">Base delay between retries in milliseconds</param>
        /// <returns>A retry policy</returns>
        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(int maxRetries, int retryDelayMs)
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    maxRetries,
                    retryAttempt => TimeSpan.FromMilliseconds(retryDelayMs * Math.Pow(2, retryAttempt - 1)),
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        // Could add logging here in the future
                        Console.WriteLine($"Retrying after {timespan.TotalSeconds:n1}s (attempt {retryAttempt})");
                    });
        }

        /// <summary>
        /// Creates a circuit breaker policy to prevent repeated calls to failing services
        /// </summary>
        /// <returns>A circuit breaker policy</returns>
        private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (outcome, timespan) =>
                    {
                        // Could add logging here in the future
                        Console.WriteLine($"Circuit breaker opened for {timespan.TotalSeconds:n1}s");
                    },
                    onReset: () =>
                    {
                        // Could add logging here in the future
                        Console.WriteLine("Circuit breaker reset");
                    });
        }
    }
}