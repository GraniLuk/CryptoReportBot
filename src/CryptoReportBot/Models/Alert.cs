using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace CryptoReportBot.Models
{
    public class Alert
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        
        [JsonPropertyName("alert_type")]
        public string? AlertType { get; set; }
        
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }
        
        [JsonPropertyName("symbol1")]
        public string? Symbol1 { get; set; }
        
        [JsonPropertyName("symbol2")]
        public string? Symbol2 { get; set; }
        
        [JsonPropertyName("price")]
        public double Price { get; set; }
        
        [JsonPropertyName("operator")]
        public string? Operator { get; set; }
        
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        
        [JsonPropertyName("triggered_date")]
        public string? TriggeredDate { get; set; }
        
        // Indicator-specific properties
        [JsonPropertyName("indicator_type")]
        public string? IndicatorType { get; set; }
        
        [JsonPropertyName("condition")]
        public string? Condition { get; set; }
        
        [JsonPropertyName("config")]
        public IndicatorConfig? Config { get; set; }
        
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
        
        [JsonPropertyName("created_date")]
        public string? CreatedDate { get; set; }
        
        [JsonPropertyName("triggers")]
        public List<object>? Triggers { get; set; }
    }
    
    public class IndicatorConfig
    {
        [JsonPropertyName("period")]
        public int Period { get; set; }
        
        [JsonPropertyName("overbought_level")]
        public double OverboughtLevel { get; set; }
        
        [JsonPropertyName("oversold_level")]
        public double OversoldLevel { get; set; }
        
        [JsonPropertyName("timeframe")]
        public string? Timeframe { get; set; }
    }
}