using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using CryptoReportBot.Models;

namespace CryptoReportBot
{
    public class Bot
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IConfigurationManager _config;
        private readonly ILogger<Bot> _logger;
        
        // Conversation handlers
        private readonly CreateAlertHandler _createAlertHandler;
        private readonly CreateGmtAlertHandler _createGmtAlertHandler;
        private readonly RemoveAlertHandler _removeAlertHandler;
        private readonly ListAlertsHandler _listAlertsHandler;
        
        // User states dictionary - simple in-memory state management (could be replaced with Redis or other distributed cache)
        public readonly Dictionary<long, UserConversationState> UserStates = new Dictionary<long, UserConversationState>();

        public Bot(
            IConfigurationManager config,
            ILogger<Bot> logger,
            CreateAlertHandler createAlertHandler,
            CreateGmtAlertHandler createGmtAlertHandler,
            RemoveAlertHandler removeAlertHandler,
            ListAlertsHandler listAlertsHandler)
        {
            _config = config;
            _logger = logger;
            _createAlertHandler = createAlertHandler;
            _createGmtAlertHandler = createGmtAlertHandler;
            _removeAlertHandler = removeAlertHandler;
            _listAlertsHandler = listAlertsHandler;
            
            // Configure culture settings similar to Python version
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            
            // Create Telegram bot client
            _botClient = new TelegramBotClient(_config.BotToken);
        }

        public async Task StartAsync()
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // Receive all update types
            };
            
            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions
            );
            
            var me = await _botClient.GetMeAsync();
            _logger.LogInformation("Bot started: {Username}", me.Username);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // Log the update
                _logger.LogInformation("Received update: {UpdateType}", update.Type);
                
                // Handle different update types
                switch (update.Type)
                {
                    case UpdateType.Message:
                        if (update.Message?.Text != null)
                        {
                            await HandleMessageAsync(update.Message);
                        }
                        break;
                        
                    case UpdateType.CallbackQuery:
                        if (update.CallbackQuery != null)
                        {
                            await HandleCallbackQueryAsync(update.CallbackQuery);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing update");
            }
        }

        private async Task HandleMessageAsync(Message message)
        {
            // Get or create user state
            var userId = message.From?.Id ?? throw new InvalidOperationException("Message sender information is missing.");
            if (!UserStates.TryGetValue(userId, out var userState))
            {
                userState = new UserConversationState();
                UserStates[userId] = userState;
            }
            
            if (!string.IsNullOrEmpty(message.Text) && message.Text.StartsWith('/'))
            {
                var command = message.Text.Split(' ')[0].ToLowerInvariant();
                switch (command)
                {
                    case "/createalert":
                        await _createAlertHandler.StartAsync(_botClient, message, userState);
                        break;
                        
                    case "/creategmtalert":
                        await _createGmtAlertHandler.StartAsync(_botClient, message, userState);
                        break;
                        
                    case "/getalerts":
                    case "/listalerts":
                        await _listAlertsHandler.HandleAsync(_botClient, message);
                        break;
                        
                    case "/removealert":
                        await _removeAlertHandler.HandleAsync(_botClient, message);
                        break;
                        
                    case "/cancel":
                        userState.ResetState();
                        await _botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Bye! Hope to talk to you again soon."
                        );
                        break;
                        
                    default:
                        await _botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Unknown command. Available commands: /createalert, /creategmtalert, /getalerts, /removealert"
                        );
                        break;
                }
            }
            else
            {
                // Process the message based on the current state
                switch (userState.ConversationState)
                {
                    case ConversationState.AwaitingSymbol:
                        await _createAlertHandler.HandleSymbolAsync(_botClient, message, userState);
                        break;
                        
                    case ConversationState.AwaitingPrice:
                        await _createAlertHandler.HandlePriceAsync(_botClient, message, userState);
                        break;
                        
                    case ConversationState.AwaitingDescription:
                        await _createAlertHandler.HandleDescriptionAsync(_botClient, message, userState);
                        break;
                        
                    default:
                        // Ignore messages when not in a conversation
                        break;
                }
            }
        }

        private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
        {
            var userId = callbackQuery.From.Id;
            if (!UserStates.TryGetValue(userId, out var userState))
            {
                userState = new UserConversationState();
                UserStates[userId] = userState;
            }
            
            // Handle callback queries
            if (callbackQuery.Data != null && callbackQuery.Data.StartsWith("delete_"))
            {
                await _removeAlertHandler.HandleCallbackQueryAsync(_botClient, callbackQuery);
            }
            else if (new[] { "greater", "lower", "greater_or_equal", "lower_or_equal" }.Contains(callbackQuery.Data))
            {
                if (userState.Type == "single")
                {
                    await _createAlertHandler.HandleOperatorAsync(_botClient, callbackQuery, userState);
                }
                else if (userState.Type == "ratio")
                {
                    await _createGmtAlertHandler.HandleOperatorAsync(_botClient, callbackQuery, userState);
                }
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error: {apiRequestException.ErrorCode} - {apiRequestException.Message}",
                _ => exception.ToString()
            };
            
            _logger.LogError(errorMessage);
            
            // If there is a conflict (another bot instance), exit
            if (exception is ApiRequestException apiEx && apiEx.ErrorCode == 409)
            {
                _logger.LogError("Another bot instance is running. Shutting down...");
                Environment.Exit(1);
            }
            
            return Task.CompletedTask;
        }
    }
}