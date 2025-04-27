namespace CryptoReportBot.Models
{
    public enum ConversationState
    {
        None,
        AwaitingSymbol,
        AwaitingOperator,
        AwaitingPrice,
        AwaitingDescription
    }

    public class UserConversationState
    {
        public ConversationState ConversationState { get; set; } = ConversationState.None;
        public string Type { get; set; }
        public string Symbol { get; set; }
        public string Symbol1 { get; set; }
        public string Symbol2 { get; set; }
        public string Operator { get; set; }
        public string Price { get; set; }
        public string Description { get; set; }

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
        }
    }
}