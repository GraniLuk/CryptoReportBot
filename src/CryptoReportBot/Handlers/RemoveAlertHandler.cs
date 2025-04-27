using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using CryptoReportBot.Models;

namespace CryptoReportBot
{
    public class RemoveAlertHandler
    {
        private readonly IConfigurationManager _config;
        private readonly HttpClient _httpClient;
        private readonly ILogger<RemoveAlertHandler> _logger;
        private readonly IAzureFunctionsClient _azureFunctionsClient;

        public RemoveAlertHandler(
            IConfigurationManager config, 
            ILogger<RemoveAlertHandler> logger,
            IAzureFunctionsClient azureFunctionsClient)
        {
            _config = config;
            _logger = logger;
            _httpClient = new HttpClient();
            _azureFunctionsClient = azureFunctionsClient;
        }

        public async Task HandleAsync(ITelegramBotClient botClient, Message message)
        {
            var alertsResponse = await _azureFunctionsClient.GetAllAlertsAsync();
            var alerts = alertsResponse?.Alerts ?? new List<Alert>();
            
            if (alerts.Count == 0)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "No alerts found to remove."
                );
                return;
            }

            var inlineKeyboard = new List<List<InlineKeyboardButton>>();
            foreach (var alert in alerts)
            {
                string buttonText;
                if (alert.Type == "ratio")
                {
                    buttonText = $"{alert.Symbol1}/{alert.Symbol2} {alert.Operator} {alert.Price}";
                }
                else
                {
                    buttonText = $"{alert.Symbol} {alert.Operator} {alert.Price}";
                }

                inlineKeyboard.Add(new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(buttonText, $"delete_{alert.Id}")
                });
            }

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "<b>Select an alert to remove:</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(inlineKeyboard)
            );
        }

        public async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery)
        {
            if (callbackQuery.Data.StartsWith("delete_"))
            {
                string alertId = callbackQuery.Data.Replace("delete_", "");
                bool success = await _azureFunctionsClient.DeleteAlertAsync(alertId);

                if (success)
                {
                    await botClient.EditMessageTextAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        text: "✅ Alert has been removed successfully!"
                    );
                }
                else
                {
                    await botClient.EditMessageTextAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        text: "❌ Failed to remove alert. Please try again later."
                    );
                }
            }
        }
    }
}