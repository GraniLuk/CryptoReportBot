namespace CryptoReportBot.Models
{
    public enum ConversationState
    {
        None,
        AwaitingSymbol,
        AwaitingOperator,
        AwaitingPrice,
        AwaitingDescription,
        // Indicator alert states
        AwaitingIndicatorSymbol,
        AwaitingIndicatorPeriod,
        AwaitingIndicatorOverbought,
        AwaitingIndicatorOversold,
        AwaitingIndicatorTimeframe,
        AwaitingIndicatorDescription,
        // Situation report states
        AwaitingSituationReportSymbol
    }

    public class UserConversationState
    {
        public ConversationState ConversationState { get; set; } = ConversationState.None;
        public string? Type { get; set; }
        public string? Symbol { get; set; }
        public string? Symbol1 { get; set; }
        public string? Symbol2 { get; set; }
        public string? Operator { get; set; }
        public string? Price { get; set; }
        public string? Description { get; set; }
        
        // Indicator alert properties
        public string? IndicatorType { get; set; }
        public int IndicatorPeriod { get; set; }
        public double IndicatorOverbought { get; set; }
        public double IndicatorOversold { get; set; }
        public string? IndicatorTimeframe { get; set; }

        public void ResetState()
        {
            ConversationState = ConversationState.None;
            Type = null;
            Symbol = null;
            Symbol1 = null;
            Symbol2 = null;
            Operator = null;
            Price = null;
            Description = null;
            IndicatorType = null;
            IndicatorPeriod = 0;
            IndicatorOverbought = 0;
            IndicatorOversold = 0;
            IndicatorTimeframe = null;
        }
    }
}