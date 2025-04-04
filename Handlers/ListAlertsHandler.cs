using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CryptoReportBot
{
    public class ListAlertsHandler
    {
        private readonly ILogger<ListAlertsHandler> _logger;
        private readonly IAzureFunctionsClient _azureFunctionsClient;

        public ListAlertsHandler(
            ILogger<ListAlertsHandler> logger,
            IAzureFunctionsClient azureFunctionsClient)
        {
            _logger = logger;
            _azureFunctionsClient = azureFunctionsClient;
        }

        public async Task HandleAsync(ITelegramBotClient botClient, Message message)
        {
            _logger.LogInformation("User {UserId} requested to list alerts", message.From.Id);
            
            // Get all alerts from API
            var alertsResponse = await _azureFunctionsClient.GetAllAlertsAsync();
            var alerts = alertsResponse?.Alerts;
            
            if (alerts == null || alerts.Count == 0)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "No alerts found or error fetching alerts."
                );
                return;
            }
            
            // Build message with alerts information
            var messageBuilder = new StringBuilder("📊 Current Price Alerts:\n\n");
            
            // Group alerts by type
            var singleAlerts = alerts.Where(a => a.Type != "ratio").ToList();
            var ratioAlerts = alerts.Where(a => a.Type == "ratio").ToList();
            
            // Add single symbol alerts
            if (singleAlerts.Any())
            {
                messageBuilder.AppendLine("🎯 Single Symbol Alerts:");
                foreach (var alert in singleAlerts)
                {
                    messageBuilder.AppendLine($"Symbol: ${alert.Symbol}");
                    messageBuilder.AppendLine($"Price: ${alert.Price}");
                    
                    // Escape < and > operators for HTML
                    string operator_text = alert.Operator
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;");
                        
                    messageBuilder.AppendLine($"Operator: {operator_text}");
                    messageBuilder.AppendLine($"Description: {alert.Description}");
                    messageBuilder.AppendLine("---------------");
                }
            }
            
            // Add ratio alerts
            if (ratioAlerts.Any())
            {
                messageBuilder.AppendLine("\n📈 Ratio Alerts:");
                foreach (var alert in ratioAlerts)
                {
                    messageBuilder.AppendLine($"Pair: {alert.Symbol1}/{alert.Symbol2}");
                    messageBuilder.AppendLine($"Ratio: {alert.Price}");
                    
                    // Escape < and > operators for HTML
                    string operator_text = alert.Operator
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;");
                        
                    messageBuilder.AppendLine($"Operator: {operator_text}");
                    messageBuilder.AppendLine($"Description: {alert.Description}");
                    messageBuilder.AppendLine("---------------");
                }
            }
            
            // Send the message with HTML formatting
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: messageBuilder.ToString(),
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
            );
        }
    }
}
