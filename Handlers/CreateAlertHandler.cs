using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace CryptoReportBot
{
    public class CreateAlertHandler
    {
        private readonly ILogger<CreateAlertHandler> _logger;
        private readonly IAzureFunctionsClient _azureFunctionsClient;

        public CreateAlertHandler(
            ILogger<CreateAlertHandler> logger,
            IAzureFunctionsClient azureFunctionsClient)
        {
            _logger = logger;
            _azureFunctionsClient = azureFunctionsClient;
        }

        public async Task StartAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            // Reset any existing state and initialize for new alert
            state.ResetState();
            state.Type = "single";
            state.ConversationState = ConversationState.AwaitingSymbol;
            
            // Create reply keyboard with common crypto symbols
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { 
                    new KeyboardButton("BTC"), 
                    new KeyboardButton("DOT"), 
                    new KeyboardButton("BNB"), 
                    new KeyboardButton("MATIC"), 
                    new KeyboardButton("FLOW"), 
                    new KeyboardButton("ATOM"), 
                    new KeyboardButton("OSMO"), 
                    new KeyboardButton("ETH"), 
                    new KeyboardButton("HBAR") 
                }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "<b>Welcome to the Crypto Report Bot!\n" +
                      "I will help you to create alert for your specific cryptocurrency.\n" +
                      "What is crypto symbol for new alert?\n\n" +
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
            if (state.ConversationState != ConversationState.AwaitingSymbol)
                return;
                
            state.Symbol = message.Text.Trim().ToUpper();
            state.ConversationState = ConversationState.AwaitingOperator;
            
            _logger.LogInformation("User {UserId} selected symbol: {Symbol}", message.From.Id, state.Symbol);
            
            // Create inline keyboard for operator selection
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Greater", "greater") },
                new[] { InlineKeyboardButton.WithCallbackData("Lower", "lower") },
                new[] { InlineKeyboardButton.WithCallbackData("Greater or equal", "greater_or_equal") },
                new[] { InlineKeyboardButton.WithCallbackData("Lower or equal", "lower_or_equal") }
            });
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"<b>You selected {state.Symbol} crypto.\nWhat operator do you want?</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: new ReplyKeyboardRemove()
            );
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "<b>Please choose:</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: keyboard
            );
        }

        public async Task HandleOperatorAsync(ITelegramBotClient botClient, CallbackQuery query, UserConversationState state)
        {
            // Process the operator selection
            var operatorMap = new Dictionary<string, string>
            {
                ["greater"] = "&gt;",
                ["lower"] = "&lt;",
                ["greater_or_equal"] = "&gt;=",
                ["lower_or_equal"] = "&lt;="
            };
            
            state.Operator = operatorMap[query.Data];
            state.ConversationState = ConversationState.AwaitingPrice;
            
            await botClient.AnswerCallbackQueryAsync(callbackQueryId: query.Id);
            
            await botClient.SendTextMessageAsync(
                chatId: query.Message.Chat.Id,
                text: $"<b>You selected {state.Operator} operator.\n" +
                      $"What price level do you want to set for {state.Symbol}?</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
            );
        }

        public async Task HandlePriceAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            if (state.ConversationState != ConversationState.AwaitingPrice)
                return;
                
            state.Price = message.Text.Trim();
            state.ConversationState = ConversationState.AwaitingDescription;
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "<b>Price noted.\nPlease add description:</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
            );
        }

        public async Task HandleDescriptionAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            if (state.ConversationState != ConversationState.AwaitingDescription)
                return;
                
            state.Description = message.Text;
            
            // Process and create the alert
            await HandleSummaryAsync(botClient, message, state);
            
            // Reset state
            state.ResetState();
        }

        private async Task HandleSummaryAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "<b>Description added successfully.\nLet's summarize your selections.</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
            );
            
            // Validate price format
            double priceFloat;
            try
            {
                // Try parsing with invariant culture (dot as decimal separator)
                if (!double.TryParse(state.Price, System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out priceFloat))
                {
                    // Try with comma as decimal separator
                    priceFloat = double.Parse(state.Price.Replace(',', '.'), 
                        System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌ Invalid price format: {ex.Message}"
                );
                return;
            }
            
            // Show summary
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"<b>Summary:</b>\n" +
                      $"Symbol: {state.Symbol}\n" +
                      $"Operator: {state.Operator}\n" +
                      $"Price: {state.Price}\n" +
                      $"Description: {state.Description}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
            );
            
            // Prepare alert data for the API call
            var alertData = new Dictionary<string, object>
            {
                ["type"] = "single",
                ["symbol"] = state.Symbol,
                ["price"] = priceFloat,
                ["operator"] = state.Operator.Replace("&gt;", ">").Replace("&lt;", "<"),
                ["description"] = state.Description
            };
            
            // Send the alert
            bool success = await _azureFunctionsClient.SendAlertRequestAsync(alertData);
            
            if (success)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "✅ Alert has been created successfully!"
                );
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Failed to create alert. Please try again later."
                );
            }
        }
    }
}
