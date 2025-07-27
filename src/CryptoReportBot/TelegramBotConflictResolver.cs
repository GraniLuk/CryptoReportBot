using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace CryptoReportBot
{
    public class TelegramBotConflictResolver
    {
        private readonly ILogger<TelegramBotConflictResolver> _logger;
        private readonly string _botToken;
        private readonly HttpClient _httpClient;

        public TelegramBotConflictResolver(string botToken, ILogger<TelegramBotConflictResolver> logger, HttpClient httpClient)
        {
            _botToken = botToken;
            _logger = logger;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Aggressively resolves bot conflicts by force-stopping any existing instances
        /// </summary>
        public async Task<bool> ForceResolveConflictsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Starting aggressive bot conflict resolution...");

                // Step 1: Get current bot info to verify token is valid
                var botInfo = await GetBotInfoAsync(cancellationToken);
                if (botInfo == null)
                {
                    _logger.LogError("Invalid bot token or unable to connect to Telegram API");
                    return false;
                }

                _logger.LogInformation("Bot info retrieved: {BotName} (@{Username})", 
                    botInfo.Value.first_name, botInfo.Value.username);

                // Step 2: Force clear any webhooks (even if they don't exist)
                await ForceDeleteWebhookAsync(cancellationToken);

                // Step 3: Try to consume any pending updates that might be blocking
                await ConsumePendingUpdatesAsync(cancellationToken);

                // Step 4: Wait a bit for any existing instances to fully stop
                _logger.LogInformation("Waiting 10 seconds for any existing instances to fully stop...");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

                // Step 5: Test if we can now get updates without conflict
                var canGetUpdates = await TestGetUpdatesAsync(cancellationToken);
                if (canGetUpdates)
                {
                    _logger.LogInformation("✅ Bot conflict resolved successfully!");
                    return true;
                }

                _logger.LogWarning("❌ Bot conflict still exists after aggressive resolution");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during aggressive conflict resolution");
                return false;
            }
        }

        /// <summary>
        /// Gets bot information to verify the token is valid
        /// </summary>
        private async Task<(string first_name, string username)?> GetBotInfoAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(
                    $"https://api.telegram.org/bot{_botToken}/getMe", cancellationToken);
                
                var jsonDoc = JsonDocument.Parse(response);
                if (jsonDoc.RootElement.GetProperty("ok").GetBoolean())
                {
                    var result = jsonDoc.RootElement.GetProperty("result");
                    return (
                        result.GetProperty("first_name").GetString() ?? "Unknown",
                        result.GetProperty("username").GetString() ?? "unknown"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get bot info");
            }
            return null;
        }

        /// <summary>
        /// Force delete webhook with multiple attempts
        /// </summary>
        private async Task ForceDeleteWebhookAsync(CancellationToken cancellationToken)
        {
            const int maxAttempts = 3;
            
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    _logger.LogInformation("Attempt {Attempt}/{MaxAttempts}: Force deleting webhook...", 
                        attempt, maxAttempts);

                    var response = await _httpClient.PostAsync(
                        $"https://api.telegram.org/bot{_botToken}/deleteWebhook?drop_pending_updates=true",
                        null, cancellationToken);

                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogInformation("Webhook deletion response: {Response}", responseContent);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("✅ Webhook deleted successfully on attempt {Attempt}", attempt);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Attempt {Attempt} to delete webhook failed", attempt);
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }

            _logger.LogWarning("Failed to delete webhook after {MaxAttempts} attempts", maxAttempts);
        }

        /// <summary>
        /// Consume any pending updates that might be blocking the bot
        /// </summary>
        private async Task ConsumePendingUpdatesAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Consuming any pending updates...");

                // Try to get updates with a very short timeout to consume them
                var response = await _httpClient.GetStringAsync(
                    $"https://api.telegram.org/bot{_botToken}/getUpdates?timeout=1&limit=100", 
                    cancellationToken);

                var jsonDoc = JsonDocument.Parse(response);
                if (jsonDoc.RootElement.GetProperty("ok").GetBoolean())
                {
                    var updates = jsonDoc.RootElement.GetProperty("result");
                    var updateCount = updates.GetArrayLength();
                    
                    if (updateCount > 0)
                    {
                        _logger.LogInformation("Found {UpdateCount} pending updates, consuming them...", updateCount);
                        
                        // Get the highest update_id to skip all pending updates
                        var highestUpdateId = 0;
                        foreach (var update in updates.EnumerateArray())
                        {
                            var updateId = update.GetProperty("update_id").GetInt32();
                            if (updateId > highestUpdateId)
                            {
                                highestUpdateId = updateId;
                            }
                        }

                        // Confirm the updates by calling getUpdates with offset
                        await _httpClient.GetStringAsync(
                            $"https://api.telegram.org/bot{_botToken}/getUpdates?offset={highestUpdateId + 1}&timeout=1", 
                            cancellationToken);

                        _logger.LogInformation("✅ Consumed {UpdateCount} pending updates", updateCount);
                    }
                    else
                    {
                        _logger.LogInformation("No pending updates found");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to consume pending updates (this might be normal)");
            }
        }

        /// <summary>
        /// Test if we can get updates without conflicts
        /// </summary>
        private async Task<bool> TestGetUpdatesAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Testing if getUpdates works without conflicts...");

                var response = await _httpClient.GetStringAsync(
                    $"https://api.telegram.org/bot{_botToken}/getUpdates?timeout=1&limit=1", 
                    cancellationToken);

                var jsonDoc = JsonDocument.Parse(response);
                var isOk = jsonDoc.RootElement.GetProperty("ok").GetBoolean();
                
                if (isOk)
                {
                    _logger.LogInformation("✅ getUpdates test successful - no conflicts detected");
                    return true;
                }
                else
                {
                    var description = jsonDoc.RootElement.TryGetProperty("description", out var desc) 
                        ? desc.GetString() : "Unknown error";
                    _logger.LogWarning("❌ getUpdates test failed: {Description}", description);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ getUpdates test failed with exception");
                return false;
            }
        }

        /// <summary>
        /// Get detailed information about any existing webhooks or conflicts
        /// </summary>
        public async Task<string> GetConflictDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            var diagnostics = new System.Text.StringBuilder();
            
            try
            {
                diagnostics.AppendLine("🔍 TELEGRAM BOT CONFLICT DIAGNOSTICS");
                diagnostics.AppendLine("=====================================");

                // Check webhook info
                var webhookResponse = await _httpClient.GetStringAsync(
                    $"https://api.telegram.org/bot{_botToken}/getWebhookInfo", cancellationToken);
                
                diagnostics.AppendLine($"📡 Webhook Info: {webhookResponse}");

                // Try to get bot info
                var botInfo = await GetBotInfoAsync(cancellationToken);
                if (botInfo.HasValue)
                {
                    diagnostics.AppendLine($"🤖 Bot: {botInfo.Value.first_name} (@{botInfo.Value.username})");
                }

                // Test getUpdates
                try
                {
                    var updatesResponse = await _httpClient.GetStringAsync(
                        $"https://api.telegram.org/bot{_botToken}/getUpdates?timeout=1&limit=1", 
                        cancellationToken);
                    diagnostics.AppendLine($"📥 getUpdates Test: SUCCESS");
                }
                catch (HttpRequestException ex)
                {
                    diagnostics.AppendLine($"📥 getUpdates Test: FAILED - {ex.Message}");
                }

                diagnostics.AppendLine("=====================================");
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"❌ Error during diagnostics: {ex.Message}");
            }

            return diagnostics.ToString();
        }
    }
}
