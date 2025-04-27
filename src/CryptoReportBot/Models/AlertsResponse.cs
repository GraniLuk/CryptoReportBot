using System.Collections.Generic;
using CryptoReportBot.Models;

namespace CryptoReportBot.Models
{
    public class AlertsResponse
    {
        public List<Alert> Alerts { get; set; } = new List<Alert>();
    }
}