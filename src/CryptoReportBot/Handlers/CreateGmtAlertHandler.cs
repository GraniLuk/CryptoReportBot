using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using CryptoReportBot.Models;

namespace CryptoReportBot
{
    public class CreateGmtAlertHandler
    {
        private readonly ILogger<CreateGmtAlertHandler> _logger;
        private readonly IAzureFunctionsClient _azureFunctionsClient;

        public CreateGmtAlertHandler(
            ILogger<CreateGmtAlertHandler> logger,
            IAzureFunctionsClient azureFunctionsClient)
        {
            _logger = logger;
            _azureFunctionsClient = azureFunctionsClient;
        }

        public async Task StartAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            // Reset any existing state and initialize for GMT/GST ratio alert
            state.ResetState();
            state.Symbol1 = "GMT";
            state.Symbol = "GMT/GST";
            state.Symbol2 = "GST";
            state.Type = "ratio";
            state.ConversationState = ConversationState.AwaitingOperator;
            
            _logger.LogInformation("User {UserId} started GMT/GST ratio alert creation", message.From.Id);
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "<b>You are creating alert for GMT/GST ratio.\nWhat operator do you want?</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: new ReplyKeyboardRemove()
            );
            
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
                      $"What ratio level do you want to set for {state.Symbol}?</b>",
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
                      $"Symbol 1: {state.Symbol1}\n" +
                      $"Symbol 2: {state.Symbol2}\n" +
                      $"Operator: {state.Operator}\n" +
                      $"Price: {state.Price}\n" +
                      $"Description: {state.Description}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
            );
            
            // Prepare alert data for the API call
            var alertData = new Dictionary<string, object>
            {
                ["type"] = "ratio",
                ["symbol1"] = state.Symbol1,
                ["symbol2"] = state.Symbol2,
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
