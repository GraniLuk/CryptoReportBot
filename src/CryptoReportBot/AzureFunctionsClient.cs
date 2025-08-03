using CryptoReportBot.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;

namespace CryptoReportBot
{
    public interface IAzureFunctionsClient
    {
        Task<bool> SendAlertRequestAsync(Dictionary<string, object> data);
        Task<AlertsResponse> GetAllAlertsAsync();
        Task<bool> DeleteAlertAsync(string alertId);
        Task<CreateIndicatorAlertResponse> CreateIndicatorAlertAsync(string jsonPayload);
        bool IsConfigured { get; }
        Task<bool> TestConnectionAsync();
    }

    public class AzureFunctionsClient : IAzureFunctionsClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfigurationManager _config;
        private readonly ILogger<AzureFunctionsClient> _logger;
        private int _consecutiveFailures = 0;
        private readonly int _maxRetryAttempts = 3;
        private readonly TimeSpan _initialRetryDelay = TimeSpan.FromSeconds(1);

        public AzureFunctionsClient(
            HttpClient httpClient,
            IConfigurationManager config,
            ILogger<AzureFunctionsClient> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;

            // Configure default timeout for HTTP operations
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        // Property to check if the client is properly configured with an API key
        public bool IsConfigured => !string.IsNullOrEmpty(_config.AzureFunctionKey);

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                if (!IsConfigured)
                {
                    _logger.LogWarning("Cannot test connection: Azure Function Key is not configured");
                    return false;
                }

                var url = _config.AzureFunctionUrl.Replace("insert_new_alert_grani", "health")
                    + $"?code={_config.AzureFunctionKey}";

                _logger.LogInformation("Testing connection to Azure Function at {BaseUrl}", new Uri(url).Host);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await _httpClient.GetAsync(url, cts.Token);
                
                var isSuccess = response.IsSuccessStatusCode;
                _logger.LogInformation(
                    "Connection test result: {Status}, StatusCode: {StatusCode}", 
                    isSuccess ? "Success" : "Failed", 
                    response.StatusCode);
                
                return isSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection to Azure Function");
                return false;
            }
        }

        public async Task<bool> SendAlertRequestAsync(Dictionary<string, object> data)
        {
            var stopwatch = Stopwatch.StartNew();
            var retryAttempt = 0;
            
            while (retryAttempt <= _maxRetryAttempts)
            {
                try
                {
                    if (!IsConfigured)
                    {
                        _logger.LogWarning("Cannot send alert: Azure Function Key is not configured");
                        return false;
                    }

                    _logger.LogInformation("Sending alert data: {Data}", JsonSerializer.Serialize(data));
                    
                    var url = $"{_config.AzureFunctionUrl}?code={_config.AzureFunctionKey}";
                    var content = new StringContent(
                        JsonSerializer.Serialize(data), 
                        Encoding.UTF8, 
                        "application/json");

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var response = await _httpClient.PostAsync(url, content, cts.Token);
                    var responseText = await response.Content.ReadAsStringAsync();
                    
                    _logger.LogInformation(
                        "Response status: {Status}, Response text: {Text}, Request duration: {Duration}ms", 
                        response.StatusCode, 
                        responseText,
                        stopwatch.ElapsedMilliseconds);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        // Reset consecutive failures counter on success
                        Interlocked.Exchange(ref _consecutiveFailures, 0);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "HTTP error response: {StatusCode}, Content: {Content}", 
                            response.StatusCode, 
                            responseText);
                            
                        // For certain status codes, we won't retry
                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            return false;
                        }
                        
                        // For server errors, we'll retry
                        retryAttempt++;
                    }
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Request timed out after {Duration}ms on attempt {Attempt}", 
                        stopwatch.ElapsedMilliseconds, retryAttempt + 1);
                    retryAttempt++;
                }
                catch (HttpRequestException ex)
                {
                    // Log details about the network error
                    string errorDetails = GetNetworkErrorDetails(ex);
                    _logger.LogError(ex, "Network error sending alert on attempt {Attempt}: {Details}", 
                        retryAttempt + 1, errorDetails);
                    
                    retryAttempt++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending alert on attempt {Attempt}", retryAttempt + 1);
                    
                    // Increment consecutive failures - this can be used to detect ongoing issues
                    Interlocked.Increment(ref _consecutiveFailures);
                    
                    retryAttempt++;
                }
                
                if (retryAttempt <= _maxRetryAttempts)
                {
                    // Calculate exponential backoff delay with jitter
                    var backoffDelay = CalculateBackoffDelay(retryAttempt);
                    _logger.LogInformation("Retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts})", 
                        backoffDelay.TotalMilliseconds, retryAttempt, _maxRetryAttempts);
                    await Task.Delay(backoffDelay);
                }
            }
            
            // If we've reached here, all retries failed
            _logger.LogError("All {MaxRetries} retry attempts failed when sending alert", _maxRetryAttempts);
            Interlocked.Increment(ref _consecutiveFailures);
            
            // Log severe warning if too many consecutive failures
            if (_consecutiveFailures > 5)
            {
                _logger.LogCritical(
                    "Detected {Count} consecutive failures in API communication. Bot stability may be compromised.",
                    _consecutiveFailures);
            }
            
            return false;
        }

        public async Task<AlertsResponse> GetAllAlertsAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var retryAttempt = 0;
            
            while (retryAttempt <= _maxRetryAttempts)
            {
                try
                {
                    if (!IsConfigured)
                    {
                        _logger.LogWarning("Cannot get alerts: Azure Function Key is not configured");
                        return new AlertsResponse 
                        { 
                            Alerts = new List<Alert>(), 
                            Message = "Function is not available - Azure Function Key not configured" 
                        };
                    }

                    var url = _config.AzureFunctionUrl
                        .Replace("insert_new_alert_grani", "get_all_alerts")
                        + $"?code={_config.AzureFunctionKey}";

                    _logger.LogInformation("Fetching alerts from: {Url}", 
                        url.Substring(0, url.IndexOf("?")));

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var response = await _httpClient.GetAsync(url, cts.Token);
                    
                    _logger.LogInformation("Get alerts response: {Status}, Request duration: {Duration}ms", 
                        response.StatusCode, stopwatch.ElapsedMilliseconds);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        _logger.LogDebug("Received alerts response: {Content}", content);
                        
                        // Reset consecutive failures counter on success
                        Interlocked.Exchange(ref _consecutiveFailures, 0);
                        
                        var alertsResponse = JsonSerializer.Deserialize<AlertsResponse>(content);
                        return alertsResponse ?? new AlertsResponse 
                        { 
                            Alerts = new List<Alert>(), 
                            Message = "Empty response from server" 
                        };
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("HTTP error response: {StatusCode}, Content: {Content}", 
                            response.StatusCode, errorContent);
                            
                        // For certain status codes, we won't retry
                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            return new AlertsResponse 
                            { 
                                Alerts = new List<Alert>(), 
                                Message = $"Error fetching alerts: {response.StatusCode}" 
                            };
                        }
                        
                        // For server errors, we'll retry
                        retryAttempt++;
                    }
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Request timed out after {Duration}ms on attempt {Attempt}", 
                        stopwatch.ElapsedMilliseconds, retryAttempt + 1);
                    retryAttempt++;
                }
                catch (HttpRequestException ex)
                {
                    // Log details about the network error
                    string errorDetails = GetNetworkErrorDetails(ex);
                    _logger.LogError(ex, "Network error fetching alerts on attempt {Attempt}: {Details}", 
                        retryAttempt + 1, errorDetails);
                    
                    retryAttempt++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching alerts on attempt {Attempt}", retryAttempt + 1);
                    Interlocked.Increment(ref _consecutiveFailures);
                    retryAttempt++;
                }
                
                if (retryAttempt <= _maxRetryAttempts)
                {
                    // Calculate exponential backoff delay with jitter
                    var backoffDelay = CalculateBackoffDelay(retryAttempt);
                    _logger.LogInformation("Retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts})", 
                        backoffDelay.TotalMilliseconds, retryAttempt, _maxRetryAttempts);
                    await Task.Delay(backoffDelay);
                }
            }
            
            // If we've reached here, all retries failed
            _logger.LogError("All {MaxRetries} retry attempts failed when fetching alerts", _maxRetryAttempts);
            Interlocked.Increment(ref _consecutiveFailures);
            
            // Log severe warning if too many consecutive failures
            if (_consecutiveFailures > 5)
            {
                _logger.LogCritical(
                    "Detected {Count} consecutive failures in API communication. Bot stability may be compromised.",
                    _consecutiveFailures);
            }
            
            return new AlertsResponse 
            { 
                Alerts = new List<Alert>(), 
                Message = "Error fetching alerts after multiple retries" 
            };
        }

        public async Task<bool> DeleteAlertAsync(string alertId)
        {
            var stopwatch = Stopwatch.StartNew();
            var retryAttempt = 0;
            
            while (retryAttempt <= _maxRetryAttempts)
            {
                try
                {
                    if (!IsConfigured)
                    {
                        _logger.LogWarning("Cannot delete alert: Azure Function Key is not configured");
                        return false;
                    }

                    var url = _config.AzureFunctionUrl
                        .Replace("insert_new_alert_grani", "remove_alert_grani")
                        + $"?code={_config.AzureFunctionKey}";
                    
                    var content = new StringContent(
                        JsonSerializer.Serialize(new { id = alertId }), 
                        Encoding.UTF8, 
                        "application/json");

                    _logger.LogInformation("Deleting alert with id: {AlertId}", alertId);
                    
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var response = await _httpClient.PostAsync(url, content, cts.Token);
                    var responseText = await response.Content.ReadAsStringAsync();
                    
                    _logger.LogInformation(
                        "Delete response status: {Status}, Response text: {Text}, Request duration: {Duration}ms", 
                        response.StatusCode, 
                        responseText,
                        stopwatch.ElapsedMilliseconds);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        // Reset consecutive failures counter on success
                        Interlocked.Exchange(ref _consecutiveFailures, 0);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("HTTP error response: {StatusCode}, Content: {Content}", 
                            response.StatusCode, responseText);
                            
                        // For certain status codes, we won't retry
                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            return false;
                        }
                        
                        // For server errors, we'll retry
                        retryAttempt++;
                    }
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Request timed out after {Duration}ms on attempt {Attempt}", 
                        stopwatch.ElapsedMilliseconds, retryAttempt + 1);
                    retryAttempt++;
                }
                catch (HttpRequestException ex)
                {
                    // Log details about the network error
                    string errorDetails = GetNetworkErrorDetails(ex);
                    _logger.LogError(ex, "Network error deleting alert on attempt {Attempt}: {Details}", 
                        retryAttempt + 1, errorDetails);
                    
                    retryAttempt++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting alert: {Id} on attempt {Attempt}", alertId, retryAttempt + 1);
                    Interlocked.Increment(ref _consecutiveFailures);
                    retryAttempt++;
                }
                
                if (retryAttempt <= _maxRetryAttempts)
                {
                    // Calculate exponential backoff delay with jitter
                    var backoffDelay = CalculateBackoffDelay(retryAttempt);
                    _logger.LogInformation("Retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts})", 
                        backoffDelay.TotalMilliseconds, retryAttempt, _maxRetryAttempts);
                    await Task.Delay(backoffDelay);
                }
            }
            
            // If we've reached here, all retries failed
            _logger.LogError("All {MaxRetries} retry attempts failed when deleting alert {Id}", _maxRetryAttempts, alertId);
            Interlocked.Increment(ref _consecutiveFailures);
            
            // Log severe warning if too many consecutive failures
            if (_consecutiveFailures > 5)
            {
                _logger.LogCritical(
                    "Detected {Count} consecutive failures in API communication. Bot stability may be compromised.",
                    _consecutiveFailures);
            }
            
            return false;
        }

        public async Task<CreateIndicatorAlertResponse> CreateIndicatorAlertAsync(string jsonPayload)
        {
            var stopwatch = Stopwatch.StartNew();
            var retryAttempt = 0;
            
            while (retryAttempt <= _maxRetryAttempts)
            {
                try
                {
                    if (!IsConfigured)
                    {
                        _logger.LogWarning("Cannot create indicator alert: Azure Function Key is not configured");
                        return new CreateIndicatorAlertResponse 
                        { 
                            Success = false, 
                            ErrorMessage = "Azure Function Key is not configured" 
                        };
                    }

                    var url = _config.AzureFunctionUrl
                        .Replace("insert_new_alert_grani", "create_indicator_alert")
                        + $"?code={_config.AzureFunctionKey}";
                    
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    _logger.LogInformation("Creating indicator alert with payload: {Payload}", jsonPayload);
                    
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var response = await _httpClient.PostAsync(url, content, cts.Token);
                    var responseText = await response.Content.ReadAsStringAsync();
                    
                    _logger.LogInformation(
                        "Create indicator alert response status: {Status}, Response text: {Text}, Request duration: {Duration}ms", 
                        response.StatusCode, 
                        responseText,
                        stopwatch.ElapsedMilliseconds);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        // Reset consecutive failures counter on success
                        Interlocked.Exchange(ref _consecutiveFailures, 0);
                        
                        var responseObject = JsonSerializer.Deserialize<CreateIndicatorAlertResponse>(responseText);
                        return responseObject ?? new CreateIndicatorAlertResponse 
                        { 
                            Success = true, 
                            Message = "Indicator alert created successfully" 
                        };
                    }
                    else
                    {
                        _logger.LogWarning("HTTP error response: {StatusCode}, Content: {Content}", 
                            response.StatusCode, responseText);
                        
                        // Try to parse error response
                        try
                        {
                            var errorResponse = JsonSerializer.Deserialize<CreateIndicatorAlertResponse>(responseText);
                            if (errorResponse != null)
                            {
                                return errorResponse;
                            }
                        }
                        catch
                        {
                            // If we can't parse the error response, create a generic one
                        }
                        
                        // For certain status codes, we won't retry
                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            return new CreateIndicatorAlertResponse 
                            { 
                                Success = false, 
                                ErrorMessage = $"HTTP {response.StatusCode}: {responseText}" 
                            };
                        }
                        
                        // For server errors, we'll retry
                        retryAttempt++;
                    }
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Request timed out after {Duration}ms on attempt {Attempt}", 
                        stopwatch.ElapsedMilliseconds, retryAttempt + 1);
                    retryAttempt++;
                }
                catch (HttpRequestException ex)
                {
                    // Log details about the network error
                    string errorDetails = GetNetworkErrorDetails(ex);
                    _logger.LogError(ex, "Network error creating indicator alert on attempt {Attempt}: {Details}", 
                        retryAttempt + 1, errorDetails);
                    
                    retryAttempt++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating indicator alert on attempt {Attempt}", retryAttempt + 1);
                    Interlocked.Increment(ref _consecutiveFailures);
                    retryAttempt++;
                }
                
                if (retryAttempt <= _maxRetryAttempts)
                {
                    // Calculate exponential backoff delay with jitter
                    var backoffDelay = CalculateBackoffDelay(retryAttempt);
                    _logger.LogInformation("Retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts})", 
                        backoffDelay.TotalMilliseconds, retryAttempt, _maxRetryAttempts);
                    await Task.Delay(backoffDelay);
                }
            }
            
            // If we've reached here, all retries failed
            _logger.LogError("All {MaxRetries} retry attempts failed when creating indicator alert", _maxRetryAttempts);
            Interlocked.Increment(ref _consecutiveFailures);
            
            // Log severe warning if too many consecutive failures
            if (_consecutiveFailures > 5)
            {
                _logger.LogCritical(
                    "Detected {Count} consecutive failures in API communication. Bot stability may be compromised.",
                    _consecutiveFailures);
            }
            
            return new CreateIndicatorAlertResponse 
            { 
                Success = false, 
                ErrorMessage = "Failed to create indicator alert after multiple retries" 
            };
        }

        // Helper method to extract detailed network error information
        private string GetNetworkErrorDetails(HttpRequestException ex)
        {
            var details = new StringBuilder($"HTTP Status: {ex.StatusCode}, ");
            
            if (ex.InnerException is SocketException socketEx)
            {
                details.Append($"Socket Error: {socketEx.SocketErrorCode}, ");
                details.Append($"Error Code: {socketEx.ErrorCode}, ");
            }
            else if (ex.InnerException != null)
            {
                details.Append($"Inner Exception: {ex.InnerException.GetType().Name}, ");
                details.Append($"Inner Message: {ex.InnerException.Message}");
            }
            
            return details.ToString();
        }

        // Calculate exponential backoff with jitter for retry mechanism
        private TimeSpan CalculateBackoffDelay(int retryAttempt)
        {
            // Calculate exponential delay: initialDelay * 2^attemptNumber
            double exponentialDelay = _initialRetryDelay.TotalMilliseconds * Math.Pow(2, retryAttempt - 1);
            
            // Add jitter (random value between 0-100% of calculated delay)
            var random = new Random();
            double jitterPercentage = random.NextDouble();  // Random value between 0.0 and 1.0
            double jitter = exponentialDelay * jitterPercentage;
            
            // Calculate final delay
            double finalDelay = exponentialDelay + jitter;
            
            // Cap the delay at 30 seconds
            return TimeSpan.FromMilliseconds(Math.Min(finalDelay, 30000));
        }
    }
}