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
using System.Diagnostics;

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
        
        // User states dictionary - simple in-memory state management
        public readonly Dictionary<long, UserConversationState> UserStates = new Dictionary<long, UserConversationState>();
        
        // Security: Allowed user IDs
        private readonly HashSet<long> _allowedUserIds;
        
        // Metrics for monitoring
        private int _totalUpdatesProcessed = 0;
        private int _totalErrorsEncountered = 0;
        private DateTime _startTime;
        private CancellationTokenSource? _pollingCts;

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
            
            // Initialize allowed user IDs
            _allowedUserIds = new HashSet<long>();
            var allowedUsers = config.AllowedUserIds?.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (allowedUsers != null)
            {
                foreach (var userId in allowedUsers)
                {
                    if (long.TryParse(userId.Trim(), out var id))
                    {
                        _allowedUserIds.Add(id);
                    }
                }
            }
            
            _logger.LogInformation("Bot configured with {Count} allowed users", _allowedUserIds.Count);
            if (_allowedUserIds.Count == 0)
            {
                _logger.LogWarning("No allowed users configured - all users will be able to access the bot. Consider setting 'allowed_user_ids' in configuration for security.");
            }
            
            // Create Telegram bot client
            _botClient = new TelegramBotClient(_config.BotToken);
            _startTime = DateTime.UtcNow;
        }

        public async Task StartAsync()
        {
            const int maxStartupRetries = 3;
            var retryDelay = TimeSpan.FromSeconds(5);
            
            for (int attempt = 1; attempt <= maxStartupRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation("Starting bot with polling approach (attempt {Attempt}/{MaxAttempts})", 
                        attempt, maxStartupRetries);
                    
                    _pollingCts = new CancellationTokenSource();
                    
                    // Clear any existing webhooks that might cause conflicts
                    await ClearWebhookAsync();
                    
                    var receiverOptions = new ReceiverOptions
                    {
                        AllowedUpdates = Array.Empty<UpdateType>(), // Receive all update types
                        ThrowPendingUpdates = true, // Skip old updates on startup
                    };
                    
                    // Start a watchdog timer to log status periodically
                    StartWatchdogTimer();
                    
                    _botClient.StartReceiving(
                        updateHandler: HandleUpdateAsync,
                        pollingErrorHandler: HandlePollingErrorAsync,
                        receiverOptions: receiverOptions,
                        cancellationToken: _pollingCts.Token
                    );
                    
                    var me = await _botClient.GetMeAsync(_pollingCts.Token);
                    _logger.LogInformation("Bot started successfully: {Username}", me.Username);
                    return; // Success, exit the retry loop
                }
                catch (ApiRequestException ex) when (ex.ErrorCode == 409)
                {
                    _logger.LogWarning("Bot conflict on startup attempt {Attempt}: {Message}", attempt, ex.Message);
                    
                    if (attempt == maxStartupRetries)
                    {
                        _logger.LogError("Failed to start bot after {MaxAttempts} attempts due to conflicts. " +
                            "Please ensure no other instances are running and try again.", maxStartupRetries);
                        throw;
                    }
                    
                    _logger.LogInformation("Waiting {Delay} seconds before retry...", retryDelay.TotalSeconds);
                    await Task.Delay(retryDelay);
                    retryDelay = TimeSpan.FromSeconds(retryDelay.TotalSeconds * 2); // Exponential backoff
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Critical error during bot startup on attempt {Attempt}", attempt);
                    
                    if (attempt == maxStartupRetries)
                    {
                        throw; // Rethrow to ensure the application terminates if bot can't start
                    }
                    
                    _logger.LogInformation("Waiting {Delay} seconds before retry...", retryDelay.TotalSeconds);
                    await Task.Delay(retryDelay);
                }
            }
        }

        public Task StopAsync()
        {
            _logger.LogInformation("Stopping bot...");
            _pollingCts?.Cancel();
            _logger.LogInformation("Bot stopped at: {Time}, Total updates processed: {Updates}, Total errors: {Errors}", 
                DateTime.UtcNow, _totalUpdatesProcessed, _totalErrorsEncountered);
            return Task.CompletedTask;
        }

        private void StartWatchdogTimer()
        {
            var timer = new Timer(_ =>
            {
                try
                {
                    var uptime = DateTime.UtcNow - _startTime;
                    _logger.LogInformation(
                        "Bot status - Uptime: {Uptime} hours, Updates processed: {Updates}, Errors: {Errors}, Active conversations: {Conversations}",
                        Math.Round(uptime.TotalHours, 2),
                        _totalUpdatesProcessed,
                        _totalErrorsEncountered,
                        UserStates.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in watchdog timer");
                }
            }, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)); // Report every 10 minutes
        }

        private async Task ClearWebhookAsync()
        {
            try
            {
                _logger.LogInformation("Clearing any existing webhooks...");
                
                // Delete webhook to ensure polling mode works
                await _botClient.DeleteWebhookAsync(dropPendingUpdates: true);
                
                _logger.LogInformation("Webhooks cleared successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear webhooks (this is usually not critical): {Message}", ex.Message);
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                Interlocked.Increment(ref _totalUpdatesProcessed);
                
                var stopwatch = Stopwatch.StartNew();
                _logger.LogInformation("Received update type: {UpdateType}", update.Type);
                
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
                
                stopwatch.Stop();
                _logger.LogDebug("Update {UpdateId} processed in {ElapsedMs}ms", 
                    update.Id, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalErrorsEncountered);
                
                // Detailed error logging
                _logger.LogError(ex, "Error processing update {UpdateType}. Details: {@Update}", 
                    update.Type, new 
                    { 
                        UpdateId = update.Id, 
                        MessageText = update.Message?.Text ?? "(null)",
                        ChatId = update.Message?.Chat?.Id ?? update.CallbackQuery?.Message?.Chat?.Id, 
                        UserName = update.Message?.From?.Username ?? update.CallbackQuery?.From?.Username ?? "(unknown)"
                    });
                
                // Try to report error to user if possible
                try
                {
                    var chatId = update.Message?.Chat?.Id ?? update.CallbackQuery?.Message?.Chat?.Id;
                    if (chatId.HasValue)
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId.Value,
                            text: "Sorry, there was an error processing your request. Please try again later.",
                            cancellationToken: cancellationToken);
                    }
                }
                catch (Exception reportEx)
                {
                    _logger.LogError(reportEx, "Error while reporting error to user");
                }
            }
        }

        private async Task HandleMessageAsync(Message message)
        {
            try 
            {
                // Get or create user state
                var userId = message.From?.Id ?? throw new InvalidOperationException("Message sender information is missing.");
                
                // Security check - only allow authorized users
                if (!IsUserAuthorized(userId, message.From.Username))
                {
                    _logger.LogWarning("Unauthorized access attempt from user {UserId} ({Username})", 
                        userId, message.From.Username ?? "unknown");
                        
                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "❌ You are not authorized to use this bot."
                    );
                    return;
                }
                
                if (!UserStates.TryGetValue(userId, out var userState))
                {
                    userState = new UserConversationState();
                    UserStates[userId] = userState;
                    _logger.LogInformation("New conversation started for user {UserId}", userId);
                }
                
                if (!string.IsNullOrEmpty(message.Text) && message.Text.StartsWith('/'))
                {
                    var command = message.Text.Split(' ')[0].ToLowerInvariant();
                    _logger.LogInformation("Processing command: {Command} from user {UserId}", command, userId);
                    
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
                        
                        case "/status":
                            // Add status command to check bot health
                            var uptime = DateTime.UtcNow - _startTime;
                            await _botClient.SendTextMessageAsync(
                                chatId: message.Chat.Id,
                                text: $"Bot is running.\nUptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m\n" +
                                      $"Total requests: {_totalUpdatesProcessed}\n" +
                                      $"Memory usage: {GC.GetTotalMemory(false) / (1024 * 1024)} MB"
                            );
                            break;
                            
                        default:
                            await _botClient.SendTextMessageAsync(
                                chatId: message.Chat.Id,
                                text: "Unknown command. Available commands: /createalert, /creategmtalert, /getalerts, /removealert, /status"
                            );
                            break;
                    }
                }
                else
                {
                    _logger.LogInformation("Processing message in state {State} from user {UserId}", 
                        userState.ConversationState, userId);
                        
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
                            _logger.LogDebug("Ignoring message, not in a conversation state. User {UserId}", userId);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleMessageAsync. ChatId: {ChatId}, UserId: {UserId}", 
                    message.Chat.Id, message.From?.Id);
                throw;
            }
        }

        private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
        {
            try
            {
                var userId = callbackQuery.From.Id;
                _logger.LogInformation("Processing callback query: {CallbackData} from user {UserId}", 
                    callbackQuery.Data, userId);
                
                // Security check - only allow authorized users
                if (!IsUserAuthorized(userId, callbackQuery.From.Username))
                {
                    _logger.LogWarning("Unauthorized callback query from user {UserId} ({Username})", 
                        userId, callbackQuery.From.Username ?? "unknown");
                        
                    await _botClient.AnswerCallbackQueryAsync(
                        callbackQueryId: callbackQuery.Id,
                        text: "You are not authorized to use this bot."
                    );
                    return;
                }
                    
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
                else
                {
                    _logger.LogWarning("Unknown callback data: {CallbackData} from user {UserId}", 
                        callbackQuery.Data, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleCallbackQueryAsync. UserId: {UserId}",
                    callbackQuery.From.Id);
                throw;
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _totalErrorsEncountered);
            
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException => 
                    $"Telegram API Error: {apiRequestException.ErrorCode} - {apiRequestException.Message}",
                _ => exception.ToString()
            };
            
            _logger.LogError("Polling error: {ErrorMessage}, StackTrace: {StackTrace}", 
                errorMessage, exception.StackTrace);
            
            // If there is a conflict (another bot instance), try to recover gracefully
            if (exception is ApiRequestException apiEx && apiEx.ErrorCode == 409)
            {
                _logger.LogWarning("Bot instance conflict detected (409). This might be due to:");
                _logger.LogWarning("1. Previous instance still running");
                _logger.LogWarning("2. Webhook configuration conflict");
                _logger.LogWarning("3. Multiple deployments running");
                _logger.LogWarning("Attempting graceful recovery...");
                
                // Instead of immediately exiting, try to clear webhooks and restart
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken); // Wait a bit
                        await ClearWebhookAsync(); // Clear any conflicting webhooks
                        _logger.LogInformation("Cleared webhooks, polling should resume automatically");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to recover from 409 error");
                        _logger.LogCritical("Unable to resolve bot conflict. Shutting down to prevent resource conflicts.");
                        Environment.Exit(1);
                    }
                }, cancellationToken);
                
                return Task.CompletedTask;
            }
            
            // For network errors, add a delay to prevent rapid reconnection attempts
            if (exception is System.Net.Http.HttpRequestException)
            {
                _logger.LogWarning("Network error detected. Waiting before reconnecting...");
                try
                {
                    Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).Wait(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation during delay
                }
            }
            
            return Task.CompletedTask;
        }

        private bool IsUserAuthorized(long userId, string? username)
        {
            // Log all access attempts for monitoring
            _logger.LogInformation("Access attempt from user {UserId} ({Username})", userId, username ?? "unknown");
            
            // If no allowed users configured, allow everyone (fallback)
            if (_allowedUserIds.Count == 0)
            {
                _logger.LogWarning("No allowed users configured - allowing all access");
                return true;
            }
            
            return _allowedUserIds.Contains(userId);
        }
        
        /// <summary>
        /// Handle incoming webhook updates from Telegram
        /// </summary>
        public async Task HandleWebhookUpdateAsync(Update update)
        {
            try
            {
                await HandleUpdateAsync(_botClient, update, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling webhook update");
                throw;
            }
        }
        
        /// <summary>
        /// Configure webhook mode instead of polling
        /// </summary>
        public async Task StartWithWebhookAsync(string webhookUrl)
        {
            try
            {
                _startTime = DateTime.UtcNow;
                _pollingCts = new CancellationTokenSource();
                
                _logger.LogInformation("Setting up webhook at: {WebhookUrl}", webhookUrl);
                
                // Set up the webhook
                await _botClient.SetWebhookAsync(
                    url: webhookUrl,
                    allowedUpdates: Array.Empty<UpdateType>(), // Receive all update types
                    cancellationToken: _pollingCts.Token
                );
                
                var me = await _botClient.GetMeAsync(_pollingCts.Token);
                _logger.LogInformation("Bot webhook configured successfully: {Username}", me.Username);
                _logger.LogInformation("Webhook URL: {WebhookUrl}", webhookUrl);
                
                // Start the watchdog timer
                StartWatchdogTimer();
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to configure webhook");
                throw;
            }
        }
    }
}