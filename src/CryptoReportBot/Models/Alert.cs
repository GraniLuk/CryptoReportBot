using System;

namespace CryptoReportBot.Models
{
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
}