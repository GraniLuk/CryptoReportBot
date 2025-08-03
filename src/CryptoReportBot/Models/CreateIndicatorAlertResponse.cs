using System.Text.Json.Serialization;

namespace CryptoReportBot.Models
{
    public class CreateIndicatorAlertResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [JsonPropertyName("message")]
        public string? Message { get; set; }
        
        [JsonPropertyName("error")]
        public string? ErrorMessage { get; set; }
        
        [JsonPropertyName("alert")]
        public Alert? Alert { get; set; }
    }
}
