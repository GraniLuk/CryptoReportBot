using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using CryptoReportBot.Models;

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
            _logger.LogInformation("User {UserId} requested to list alerts", message.From?.Id ?? 0);
            
            // Check if Azure Functions client is configured
            if (!_azureFunctionsClient.IsConfigured)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Alert listing is currently unavailable due to configuration issues. Please try again later or contact the administrator.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
                _logger.LogWarning("Failed to list alerts because AzureFunctionsClient is not configured");
                return;
            }
            
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
            var messageBuilder = new StringBuilder("📊 Current Alerts:\n\n");
            
            // Group alerts by type
            var singleAlerts = alerts.Where(a => a.AlertType == "price" && a.Type == "single").ToList();
            var ratioAlerts = alerts.Where(a => a.AlertType == "price" && a.Type == "ratio").ToList();
            var indicatorAlerts = alerts.Where(a => a.AlertType == "indicator").ToList();
            
            // Add single symbol alerts
            if (singleAlerts.Any())
            {
                messageBuilder.AppendLine("🎯 Single Symbol Price Alerts:");
                foreach (var alert in singleAlerts)
                {
                    messageBuilder.AppendLine($"Symbol: {alert.Symbol ?? "Unknown"}");
                    messageBuilder.AppendLine($"Target Price: ${alert.Price}");
                    
                    // Add current value information
                    if (alert.CurrentValue?.CurrentPrice != null)
                    {
                        messageBuilder.AppendLine($"Current Price: ${alert.CurrentValue.CurrentPrice:F4}");
                        
                        if (alert.CurrentValue.PriceRange != null)
                        {
                            messageBuilder.AppendLine($"24h Range: ${alert.CurrentValue.PriceRange.Low:F4}-${alert.CurrentValue.PriceRange.High:F4}");
                        }
                    }
                    
                    // Escape < and > operators for HTML, handle null operator
                    string operator_text = (alert.Operator ?? "")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;");
                        
                    messageBuilder.AppendLine($"Operator: {operator_text}");
                    messageBuilder.AppendLine($"Description: {alert.Description ?? "No description"}");
                    messageBuilder.AppendLine("---------------");
                }
            }
            
            // Add ratio alerts
            if (ratioAlerts.Any())
            {
                messageBuilder.AppendLine("\n📈 Ratio Price Alerts:");
                foreach (var alert in ratioAlerts)
                {
                    // Handle potential null values for Symbol1 and Symbol2
                    string symbol1 = alert.Symbol1 ?? "Unknown";
                    string symbol2 = alert.Symbol2 ?? "Unknown";
                    messageBuilder.AppendLine($"Pair: {symbol1}/{symbol2}");
                    messageBuilder.AppendLine($"Target Ratio: {alert.Price}");
                    
                    // Add current value information for ratio alerts
                    if (alert.CurrentValue?.CurrentPrice != null)
                    {
                        messageBuilder.AppendLine($"Current Ratio: {alert.CurrentValue.CurrentPrice:F4}");
                        
                        if (alert.CurrentValue.PriceRange != null)
                        {
                            messageBuilder.AppendLine($"24h Range: {alert.CurrentValue.PriceRange.Low:F4}-{alert.CurrentValue.PriceRange.High:F4}");
                        }
                    }
                    
                    // Escape < and > operators for HTML, handle null operator
                    string operator_text = (alert.Operator ?? "")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;");
                        
                    messageBuilder.AppendLine($"Operator: {operator_text}");
                    messageBuilder.AppendLine($"Description: {alert.Description ?? "No description"}");
                    messageBuilder.AppendLine("---------------");
                }
            }
            
            // Add indicator alerts
            if (indicatorAlerts.Any())
            {
                messageBuilder.AppendLine("\n📊 RSI Indicator Alerts:");
                foreach (var alert in indicatorAlerts)
                {
                    messageBuilder.AppendLine($"Symbol: {alert.Symbol ?? "Unknown"}");
                    messageBuilder.AppendLine($"Indicator: {(alert.IndicatorType ?? "").ToUpper()}");
                    messageBuilder.AppendLine($"Condition: {alert.Condition ?? "Unknown"}");
                    
                    // Add current RSI and price information
                    if (alert.CurrentValue != null)
                    {
                        if (alert.CurrentValue.CurrentRsi != null)
                        {
                            messageBuilder.AppendLine($"Current RSI: {alert.CurrentValue.CurrentRsi:F2}");
                            
                            // Add RSI status with emoji
                            if (alert.CurrentValue.RsiStatus != null)
                            {
                                string statusEmoji = alert.CurrentValue.RsiStatus.IsOverbought ? "🔴" :
                                                   alert.CurrentValue.RsiStatus.IsOversold ? "🟢" : "⚪";
                                string statusText = alert.CurrentValue.RsiStatus.IsOverbought ? "Overbought" :
                                                  alert.CurrentValue.RsiStatus.IsOversold ? "Oversold" : "Neutral";
                                messageBuilder.AppendLine($"Status: {statusEmoji} {statusText}");
                            }
                        }
                        
                        if (alert.CurrentValue.CurrentPrice != null)
                        {
                            messageBuilder.AppendLine($"Current Price: ${alert.CurrentValue.CurrentPrice:F4}");
                        }
                    }
                    
                    // Display config information if available
                    if (alert.Config != null)
                    {
                        messageBuilder.AppendLine($"Config: RSI({alert.Config.Period}) - OB:{alert.Config.OverboughtLevel} OS:{alert.Config.OversoldLevel}");
                        messageBuilder.AppendLine($"Timeframe: {alert.Config.Timeframe ?? "Unknown"}");
                    }
                    
                    messageBuilder.AppendLine($"Description: {alert.Description ?? "No description"}");
                    messageBuilder.AppendLine($"Enabled: {(alert.Enabled ? "Yes" : "No")}");
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
