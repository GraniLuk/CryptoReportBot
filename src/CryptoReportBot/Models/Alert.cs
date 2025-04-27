using System;
using System.Text.Json.Serialization;

namespace CryptoReportBot.Models
{
    public class Alert
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        [JsonPropertyName("type")]
        public string Type { get; set; }
        
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }
        
        [JsonPropertyName("symbol1")]
        public string Symbol1 { get; set; }
        
        [JsonPropertyName("symbol2")]
        public string Symbol2 { get; set; }
        
        [JsonPropertyName("price")]
        public double Price { get; set; }
        
        [JsonPropertyName("operator")]
        public string Operator { get; set; }
        
        [JsonPropertyName("description")]
        public string Description { get; set; }
        
        [JsonPropertyName("triggered_date")]
        public string TriggeredDate { get; set; }
    }
}