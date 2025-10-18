using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using CryptoReportBot.Models;

namespace CryptoReportBot
{
    public class CreateSituationReportHandler
    {
        private readonly ILogger<CreateSituationReportHandler> _logger;
        private readonly IAzureFunctionsClient _azureFunctionsClient;

        public CreateSituationReportHandler(
            ILogger<CreateSituationReportHandler> logger,
            IAzureFunctionsClient azureFunctionsClient)
        {
            _logger = logger;
            _azureFunctionsClient = azureFunctionsClient;
        }

        public async Task StartAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            _logger.LogInformation("User {UserId} started creating situation report", message.From?.Id ?? 0);

            // Check if Azure Functions client is configured
            if (!_azureFunctionsClient.IsConfigured)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Situation report is currently unavailable due to configuration issues. Please try again later or contact the administrator.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
                _logger.LogWarning("Failed to create situation report because AzureFunctionsClient is not configured");
                return;
            }

            // Reset any existing state and initialize for situation report
            state.ResetState();
            state.Type = "situation_report";
            state.ConversationState = ConversationState.AwaitingSituationReportSymbol;
            
            // Create reply keyboard with common crypto symbols
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { 
                    new KeyboardButton("BTC"), 
                    new KeyboardButton("ETH"), 
                    new KeyboardButton("BNB"), 
                    new KeyboardButton("DOT"),  
                    new KeyboardButton("SOL"),
                    new KeyboardButton("VIRTUAL") 
                }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "<b>📊 Welcome to Crypto Situation Report!\n\n" +
                      "I will generate a comprehensive situation report for your cryptocurrency.\n" +
                      "The report will be automatically saved to OneDrive and sent to your Telegram.\n\n" +
                      "What crypto symbol do you want a situation report for?\n\n" +
                      "You can either:\n" +
                      "• Select from the common options below\n" +
                      "• Type any crypto symbol you want</b>",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: keyboard
            );
        }

        public async Task HandleSymbolAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            // Only process if we're expecting a symbol for situation report
            if (state.ConversationState != ConversationState.AwaitingSituationReportSymbol)
                return;
                
            state.Symbol = message.Text?.Trim().ToUpper();
            
            // Validate symbol is not empty
            if (string.IsNullOrEmpty(state.Symbol))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Please provide a valid crypto symbol.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
                return;
            }
            
            _logger.LogInformation("User {UserId} selected symbol: {Symbol} for situation report", message.From?.Id ?? 0, state.Symbol);
            
            // Immediately process the request
            await ProcessSituationReportAsync(botClient, message, state);
            
            // Reset state
            state.ResetState();
        }

        private async Task ProcessSituationReportAsync(ITelegramBotClient botClient, Message message, UserConversationState state)
        {
            // Validate symbol exists
            if (string.IsNullOrEmpty(state.Symbol))
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Invalid symbol. Please try again.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    replyMarkup: new ReplyKeyboardRemove()
                );
                return;
            }
            
            // Check again if Azure Functions client is still configured
            if (!_azureFunctionsClient.IsConfigured)
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "❌ Situation report is currently unavailable due to configuration issues. Please try again later or contact the administrator.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    replyMarkup: new ReplyKeyboardRemove()
                );
                _logger.LogWarning("Failed to create situation report because AzureFunctionsClient is not configured");
                return;
            }
            
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"<b>📊 Generating Situation Report for {state.Symbol}...</b>\n\n" +
                      $"🔄 Processing your request...\n" +
                      $"📋 The report will include comprehensive analysis\n" +
                      $"💾 Automatically saving to OneDrive\n" +
                      $"📤 Will be sent to your Telegram\n\n" +
                      $"Please wait a moment...",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyMarkup: new ReplyKeyboardRemove()
            );

            try
            {
                // Call the Azure Function API for situation report
                var success = await _azureFunctionsClient.RequestSituationReportAsync(state.Symbol);

                if (success)
                {
                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: $"🎉 <b>Situation Report for {state.Symbol} Requested Successfully!</b>\n\n" +
                              "📊 Your comprehensive crypto situation report is being generated.\n" +
                              "💾 The report will be saved to OneDrive automatically.\n" +
                              "📤 You'll receive the report via Telegram once it's ready.\n\n" +
                              "⏱️ This usually takes a few moments. Please check your messages shortly!",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                    );

                    _logger.LogInformation("Successfully requested situation report for user {UserId}: {Symbol}", 
                        message.From?.Id ?? 0, state.Symbol);
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: $"❌ <b>Failed to generate situation report for {state.Symbol}</b>\n\n" +
                              "This could be due to:\n" +
                              "• Invalid symbol\n" +
                              "• Temporary service issues\n" +
                              "• Network connectivity problems\n\n" +
                              "Please try again later or contact the administrator.",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                    );

                    _logger.LogWarning("Failed to request situation report for user {UserId}: {Symbol}", 
                        message.From?.Id ?? 0, state.Symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting situation report for user {UserId}, symbol: {Symbol}", 
                    message.From?.Id ?? 0, state.Symbol);
                
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"❌ <b>An error occurred while generating the situation report for {state.Symbol}.</b>\n\n" +
                          "Please try again later or contact the administrator.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
            }
        }
    }
}
