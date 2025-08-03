using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using CryptoReportBot.Models;

namespace CryptoReportBot
{
    public class CreateIndicatorAlertHandler
    {
        private readonly ILogger<CreateIndicatorAlertHandler> _logger;
        private readonly IAzureFunctionsClient _azureFunctionsClient;

        public CreateIndicatorAlertHandler(
            ILogger<CreateIndicatorAlertHandler> logger,
            IAzureFunctionsClient azureFunctionsClient)
        {
            _logger = logger;
            _azureFunctionsClient = azureFunctionsClient;
        }

        public async Task StartAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            _logger.LogInformation("User {UserId} started creating indicator alert", message.From?.Id ?? 0);

            // Check if Azure Functions client is configured
            if (!_azureFunctionsClient.IsConfigured)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Alert creation is currently unavailable due to configuration issues. Please try again later or contact the administrator.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
                _logger.LogWarning("Failed to create indicator alert because AzureFunctionsClient is not configured");
                return;
            }

            // Reset any existing state and initialize for new indicator alert
            state.ResetState();
            state.Type = "indicator";
            state.IndicatorType = "rsi";
            state.ConversationState = ConversationState.AwaitingIndicatorSymbol;
            
            // Create reply keyboard with common crypto symbols
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { 
                    new KeyboardButton("BTC"), 
                    new KeyboardButton("ETH"), 
                    new KeyboardButton("BNB"), 
                    new KeyboardButton("DOT"), 
                    new KeyboardButton("MATIC"), 
                    new KeyboardButton("ATOM"), 
                    new KeyboardButton("OSMO"), 
                    new KeyboardButton("HBAR"),
                    new KeyboardButton("FLOW") 
                }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "<b>📊 Welcome to RSI Indicator Alert Setup!\n\n" +
                      "I will help you create an RSI indicator alert for your cryptocurrency.\n" +
                      "RSI alerts will notify you when the RSI crosses overbought or oversold levels.\n\n" +
                      "What crypto symbol do you want to monitor?\n\n" +
                      "You can either:\n" +
                      "• Select from the common options below\n" +
                      "• Type any crypto symbol you want</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: keyboard
            );
        }

        public async Task HandleSymbolAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            // Only process if we're expecting a symbol
            if (state.ConversationState != ConversationState.AwaitingIndicatorSymbol)
                return;
                
            state.Symbol = message.Text?.Trim().ToUpper();
            state.ConversationState = ConversationState.AwaitingIndicatorPeriod;
            
            _logger.LogInformation("User {UserId} selected symbol: {Symbol} for indicator alert", message.From?.Id ?? 0, state.Symbol);
            
            // Create reply keyboard with common RSI periods
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { 
                    new KeyboardButton("14"), 
                    new KeyboardButton("21"), 
                    new KeyboardButton("7"), 
                    new KeyboardButton("9") 
                }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"<b>✅ You selected {state.Symbol} for RSI monitoring.\n\n" +
                      $"📈 What RSI period do you want to use?\n\n" +
                      $"Common periods:\n" +
                      $"• 14 (most popular, default)\n" +
                      $"• 21 (longer term)\n" +
                      $"• 7 (shorter term)\n" +
                      $"• 9 (short-medium term)\n\n" +
                      $"Or type any number ≥ 2:</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: keyboard
            );
        }

        public async Task HandlePeriodAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            if (state.ConversationState != ConversationState.AwaitingIndicatorPeriod)
                return;

            if (!int.TryParse(message.Text?.Trim(), out int period) || period < 2)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Invalid period! Please enter a number ≥ 2 (e.g., 14, 21, 7)."
                );
                return;
            }

            state.IndicatorPeriod = period;
            state.ConversationState = ConversationState.AwaitingIndicatorOverbought;
            
            // Create reply keyboard with common overbought levels
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { 
                    new KeyboardButton("70"), 
                    new KeyboardButton("75"), 
                    new KeyboardButton("80"), 
                    new KeyboardButton("85") 
                }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"<b>✅ RSI period set to {period}.\n\n" +
                      $"📊 What overbought level do you want? (1-100)\n\n" +
                      $"When RSI goes above this level, it's considered overbought:\n" +
                      $"• 70 (most common)\n" +
                      $"• 75 (moderate)\n" +
                      $"• 80 (conservative)\n" +
                      $"• 85 (very conservative)\n\n" +
                      $"Or type any number between 1-100:</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: keyboard
            );
        }

        public async Task HandleOverboughtAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            if (state.ConversationState != ConversationState.AwaitingIndicatorOverbought)
                return;

            if (!double.TryParse(message.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double overbought) 
                || overbought <= 0 || overbought > 100)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Invalid overbought level! Please enter a number between 1 and 100 (e.g., 70, 75, 80)."
                );
                return;
            }

            state.IndicatorOverbought = overbought;
            state.ConversationState = ConversationState.AwaitingIndicatorOversold;
            
            // Create reply keyboard with common oversold levels
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { 
                    new KeyboardButton("30"), 
                    new KeyboardButton("25"), 
                    new KeyboardButton("20"), 
                    new KeyboardButton("15") 
                }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"<b>✅ Overbought level set to {overbought}.\n\n" +
                      $"📉 What oversold level do you want? (0-99, must be less than {overbought})\n\n" +
                      $"When RSI goes below this level, it's considered oversold:\n" +
                      $"• 30 (most common)\n" +
                      $"• 25 (moderate)\n" +
                      $"• 20 (conservative)\n" +
                      $"• 15 (very conservative)\n\n" +
                      $"Or type any number between 0-{(int)overbought - 1}:</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: keyboard
            );
        }

        public async Task HandleOversoldAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            if (state.ConversationState != ConversationState.AwaitingIndicatorOversold)
                return;

            if (!double.TryParse(message.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double oversold) 
                || oversold < 0 || oversold >= 100)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Invalid oversold level! Please enter a number between 0 and 99."
                );
                return;
            }

            if (oversold >= state.IndicatorOverbought)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌ Oversold level ({oversold}) must be less than overbought level ({state.IndicatorOverbought})!"
                );
                return;
            }

            state.IndicatorOversold = oversold;
            state.ConversationState = ConversationState.AwaitingIndicatorTimeframe;
            
            // Create reply keyboard with common timeframes
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { 
                    new KeyboardButton("5m"), 
                    new KeyboardButton("15m"), 
                    new KeyboardButton("1h"), 
                    new KeyboardButton("4h") 
                },
                new[] { 
                    new KeyboardButton("1d"), 
                    new KeyboardButton("30m"), 
                    new KeyboardButton("2h"), 
                    new KeyboardButton("1w") 
                }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"<b>✅ Oversold level set to {oversold}.\n\n" +
                      $"⏱️ What timeframe do you want to monitor?\n\n" +
                      $"Common timeframes:\n" +
                      $"• 5m, 15m, 30m (short-term)\n" +
                      $"• 1h, 2h, 4h (medium-term)\n" +
                      $"• 1d, 1w (long-term)\n\n" +
                      $"Valid options: 1m, 3m, 5m, 15m, 30m, 1h, 2h, 4h, 6h, 8h, 12h, 1d, 3d, 1w, 1M</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: keyboard
            );
        }

        public async Task HandleTimeframeAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            if (state.ConversationState != ConversationState.AwaitingIndicatorTimeframe)
                return;

            string timeframe = message.Text?.Trim().ToLower() ?? "";
            string[] validTimeframes = { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M" };
            
            if (Array.IndexOf(validTimeframes, timeframe) == -1)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌ Invalid timeframe! Valid options: {string.Join(", ", validTimeframes)}"
                );
                return;
            }

            state.IndicatorTimeframe = timeframe;
            state.ConversationState = ConversationState.AwaitingIndicatorDescription;
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"<b>✅ Timeframe set to {timeframe}.\n\n" +
                      $"📝 Finally, please add a description for this alert:\n\n" +
                      $"Examples:\n" +
                      $"• \"{state.Symbol} RSI alert for swing trading\"\n" +
                      $"• \"Monitor {state.Symbol} RSI for entry signals\"\n" +
                      $"• \"Daily {state.Symbol} RSI threshold alert\"</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: new ReplyKeyboardRemove()
            );
        }

        public async Task HandleDescriptionAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            if (state.ConversationState != ConversationState.AwaitingIndicatorDescription)
                return;
                
            state.Description = message.Text?.Trim();
            
            // Process and create the alert
            await HandleSummaryAsync(botClient, message, state);
            
            // Reset state
            state.ResetState();
        }

        private async Task HandleSummaryAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            // Check again if Azure Functions client is still configured
            if (!_azureFunctionsClient.IsConfigured)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Alert creation is currently unavailable due to configuration issues. Please try again later or contact the administrator.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
                _logger.LogWarning("Failed to create indicator alert because AzureFunctionsClient is not configured");
                return;
            }
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "<b>✅ Description added successfully.\nLet's summarize your RSI alert configuration.</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
            );
            
            // Show summary
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"<b>📊 RSI Indicator Alert Summary:</b>\n\n" +
                      $"🔸 <b>Symbol:</b> {state.Symbol}\n" +
                      $"🔸 <b>Indicator:</b> RSI({state.IndicatorPeriod})\n" +
                      $"🔸 <b>Timeframe:</b> {state.IndicatorTimeframe}\n" +
                      $"🔸 <b>Overbought Level:</b> {state.IndicatorOverbought}\n" +
                      $"🔸 <b>Oversold Level:</b> {state.IndicatorOversold}\n" +
                      $"🔸 <b>Description:</b> {state.Description}\n\n" +
                      $"🔔 <b>You'll be notified when RSI crosses these threshold levels!</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
            );

            // Create the request payload
            var requestPayload = new
            {
                symbol = state.Symbol,
                indicator_type = state.IndicatorType,
                config = new
                {
                    period = state.IndicatorPeriod,
                    overbought_level = state.IndicatorOverbought,
                    oversold_level = state.IndicatorOversold,
                    timeframe = state.IndicatorTimeframe
                },
                description = state.Description ?? $"{state.Symbol} RSI threshold monitoring alert",
                enabled = true
            };

            string jsonPayload = JsonSerializer.Serialize(requestPayload);

            try
            {
                // Call the Azure Function API
                var response = await _azureFunctionsClient.CreateIndicatorAlertAsync(jsonPayload);

                if (response.Success)
                {
                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "🎉 <b>RSI Indicator Alert Created Successfully!</b>\n\n" +
                              "Your alert is now active and monitoring the specified RSI levels. " +
                              "You'll receive notifications when the RSI crosses your defined thresholds.",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                    );

                    _logger.LogInformation("Successfully created indicator alert for user {UserId}: {Symbol} RSI({Period}) {Timeframe}", 
                        message.From?.Id ?? 0, state.Symbol, state.IndicatorPeriod, state.IndicatorTimeframe);
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: $"❌ <b>Failed to create indicator alert:</b>\n{response.ErrorMessage ?? "Unknown error"}\n\nPlease try again later or contact the administrator.",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                    );

                    _logger.LogWarning("Failed to create indicator alert for user {UserId}: {Error}", 
                        message.From?.Id ?? 0, response.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating indicator alert for user {UserId}", message.From?.Id ?? 0);
                
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ <b>An error occurred while creating the indicator alert.</b>\n\nPlease try again later or contact the administrator.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
            }
        }
    }
}
