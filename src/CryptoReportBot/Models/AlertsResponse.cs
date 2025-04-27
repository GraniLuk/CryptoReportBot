using System.Collections.Generic;
using System.Text.Json.Serialization;
using CryptoReportBot.Models;

namespace CryptoReportBot.Models
{
    public class AlertsResponse
    {
        [JsonPropertyName("alerts")]
        public List<Alert> Alerts { get; set; } = new List<Alert>();
    }
}