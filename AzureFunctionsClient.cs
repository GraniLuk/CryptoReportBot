using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CryptoReportBot
{
    public interface IAzureFunctionsClient
    {
        Task<bool> SendAlertRequestAsync(Dictionary<string, object> data);
        Task<AlertsResponse> GetAllAlertsAsync();
        Task<bool> DeleteAlertAsync(string alertId);
    }

    public class AlertsResponse
    {
        public List<Alert> Alerts { get; set; } = new List<Alert>();
    }

    public class Alert
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Symbol { get; set; }
        public string Symbol1 { get; set; }
        public string Symbol2 { get; set; }
        public double Price { get; set; }
        public string Operator { get; set; }
        public string Description { get; set; }
    }

    public class AzureFunctionsClient : IAzureFunctionsClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfigurationManager _config;
        private readonly ILogger<AzureFunctionsClient> _logger;

        public AzureFunctionsClient(
            HttpClient httpClient,
            IConfigurationManager config,
            ILogger<AzureFunctionsClient> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        public async Task<bool> SendAlertRequestAsync(Dictionary<string, object> data)
        {
            try
            {
                _logger.LogInformation("Sending alert data: {Data}", JsonSerializer.Serialize(data));
                
                var url = $"{_config.AzureFunctionUrl}?code={_config.AzureFunctionKey}";
                var content = new StringContent(
                    JsonSerializer.Serialize(data), 
                    Encoding.UTF8, 
                    "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseText = await response.Content.ReadAsStringAsync();
                
                _logger.LogInformation(
                    "Response status: {Status}, Response text: {Text}", 
                    response.StatusCode, 
                    responseText);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending alert");
                return false;
            }
        }

        public async Task<AlertsResponse> GetAllAlertsAsync()
        {
            try
            {
                var url = _config.AzureFunctionUrl
                    .Replace("insert_new_alert_grani", "get_all_alerts")
                    + $"?code={_config.AzureFunctionKey}";

                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<AlertsResponse>(content);
                }
                else
                {
                    _logger.LogError("Error getting alerts: {Status}", response.StatusCode);
                    return new AlertsResponse();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching alerts");
                return new AlertsResponse();
            }
        }

        public async Task<bool> DeleteAlertAsync(string alertId)
        {
            try
            {
                var url = _config.AzureFunctionUrl
                    .Replace("insert_new_alert_grani", "delete_alert")
                    + $"?code={_config.AzureFunctionKey}";
                
                var content = new StringContent(
                    JsonSerializer.Serialize(new { guid = alertId }), 
                    Encoding.UTF8, 
                    "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseText = await response.Content.ReadAsStringAsync();
                
                _logger.LogInformation(
                    "Delete response status: {Status}, Response text: {Text}", 
                    response.StatusCode, 
                    responseText);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting alert: {Id}", alertId);
                return false;
            }
        }
    }
}
