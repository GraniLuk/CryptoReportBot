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

namespace CryptoReportBot
{
    public class RemoveAlertHandler
    {
        private readonly IConfigurationManager _config;
        private readonly HttpClient _httpClient;
        private readonly ILogger<RemoveAlertHandler> _logger;

        public RemoveAlertHandler(IConfigurationManager config, ILogger<RemoveAlertHandler> logger)
        {
            _config = config;
            _logger = logger;
            _httpClient = new HttpClient();
        }

        public async Task HandleAsync(ITelegramBotClient botClient, Message message)
        {
            var alertsResponse = await GetAllAlertsAsync();
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
                bool success = await DeleteAlertAsync(alertId);

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

        private async Task<AlertsResponse> GetAllAlertsAsync()
        {
            try
            {
                // Construct the URL by replacing insert_new_alert_grani with get_all_alerts
                string url = _config.AzureFunctionUrl.Replace("insert_new_alert_grani", "get_all_alerts");
                string functionKey = _config.AzureFunctionKey;

                var response = await _httpClient.GetAsync($"{url}?code={functionKey}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("Received alerts response: {Content}", content);
                    var alertsResponse = JsonSerializer.Deserialize<AlertsResponse>(content);
                    return alertsResponse ?? new AlertsResponse();
                }
                else
                {
                    _logger.LogError($"Error getting alerts: {response.StatusCode}");
                    return new AlertsResponse();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching alerts");
                return new AlertsResponse();
            }
        }

        private async Task<bool> DeleteAlertAsync(string alertId)
        {
            try
            {
                // Construct the URL by replacing insert_new_alert_grani with delete_alert
                string url = _config.AzureFunctionUrl.Replace("insert_new_alert_grani", "delete_alert");
                string functionKey = _config.AzureFunctionKey;

                var response = await _httpClient.PostAsJsonAsync(
                    $"{url}?code={functionKey}",
                    new { guid = alertId }
                );

                var responseText = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Delete response status: {response.StatusCode}, Response text: {responseText}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting alert");
                return false;
            }
        }
    }
}
