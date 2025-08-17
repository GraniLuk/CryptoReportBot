using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoReportBot
{
    public interface IConfigurationManager
    {
        string BotToken { get; }
        string AzureFunctionUrl { get; }
        string? AzureFunctionKey { get; } // Made nullable to handle missing configuration
        string? AllowedUserIds { get; }
        string CryptoReportsApiUrl { get; }
        string? CryptoReportsApiKey { get; }
    }

    public class ConfigurationManager : IConfigurationManager
    {
        private readonly ILogger<ConfigurationManager> _logger;
        private string? _botToken = null;
        private string? _azureFunctionUrl = null;
        private string? _azureFunctionKey = null;
        private string? _allowedUserIds = null;
        private string? _cryptoReportsApiUrl = null;
        private string? _cryptoReportsApiKey = null;
        private bool _secretsLoaded = false;
        private readonly object _lockObject = new object();

        public ConfigurationManager(ILogger<ConfigurationManager> logger)
        {
            _logger = logger;
            _logger.LogInformation("ConfigurationManager initialized - will load from environment variables");
        }

        public string BotToken 
        { 
            get 
            {
                EnsureSecretsLoaded();
                return _botToken ?? throw new InvalidOperationException("Bot token not configured");
            }
        }
        
        public string AzureFunctionUrl 
        { 
            get 
            {
                EnsureSecretsLoaded();
                return _azureFunctionUrl ?? throw new InvalidOperationException("Azure Function URL not configured");
            }
        }
        
        public string? AzureFunctionKey 
        { 
            get 
            {
                EnsureSecretsLoaded();
                return _azureFunctionKey;
            }
        }
        
        public string? AllowedUserIds 
        { 
            get 
            {
                EnsureSecretsLoaded();
                return _allowedUserIds;
            }
        }

        public string CryptoReportsApiUrl 
        { 
            get 
            {
                EnsureSecretsLoaded();
                return _cryptoReportsApiUrl ?? throw new InvalidOperationException("Crypto Reports API URL not configured");
            }
        }

        public string? CryptoReportsApiKey 
        { 
            get 
            {
                EnsureSecretsLoaded();
                return _cryptoReportsApiKey;
            }
        }

        private void EnsureSecretsLoaded()
        {
            if (!_secretsLoaded)
            {
                lock (_lockObject)
                {
                    if (!_secretsLoaded)
                    {
                        LoadSecrets();
                        _secretsLoaded = true;
                    }
                }
            }
        }

        private void LoadSecrets()
        {
            try
            {
                _logger.LogInformation("Loading configuration from environment variables");
                
                _botToken = Environment.GetEnvironmentVariable("alerts_bot_token");
                _azureFunctionUrl = Environment.GetEnvironmentVariable("azure_function_url");
                _azureFunctionKey = Environment.GetEnvironmentVariable("azure_function_key");
                _allowedUserIds = Environment.GetEnvironmentVariable("allowed_user_ids");
                _cryptoReportsApiUrl = Environment.GetEnvironmentVariable("crypto_reports_api_url");
                _cryptoReportsApiKey = Environment.GetEnvironmentVariable("crypto_reports_api_key");
                
                _logger.LogInformation("Environment variables loaded - Bot token exists: {HasToken}, URL exists: {HasUrl}, Key exists: {HasKey}, Allowed users exists: {HasAllowedUsers}, Crypto Reports API exists: {HasCryptoReportsApi}, Crypto Reports API Key exists: {HasCryptoReportsApiKey}",
                    !string.IsNullOrEmpty(_botToken),
                    !string.IsNullOrEmpty(_azureFunctionUrl),
                    !string.IsNullOrEmpty(_azureFunctionKey),
                    !string.IsNullOrEmpty(_allowedUserIds),
                    !string.IsNullOrEmpty(_cryptoReportsApiUrl),
                    !string.IsNullOrEmpty(_cryptoReportsApiKey));
                
                // Validate that required secrets were loaded
                ValidateRequiredSecrets();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from environment variables");
                // Don't rethrow - let the application handle missing configuration gracefully
            }
        }

        private void ValidateRequiredSecrets()
        {
            var missingSecrets = new List<string>();
            
            if (string.IsNullOrEmpty(_botToken))
                missingSecrets.Add("Bot Token (alerts_bot_token)");
            
            if (string.IsNullOrEmpty(_azureFunctionUrl))
                missingSecrets.Add("Azure Function URL (azure_function_url)");
            
            if (string.IsNullOrEmpty(_cryptoReportsApiUrl))
                missingSecrets.Add("Crypto Reports API URL (crypto_reports_api_url)");
            
            if (missingSecrets.Any())
            {
                var message = $"Missing required configuration: {string.Join(", ", missingSecrets)}. " +
                             "Please set these values in environment variables.";
                _logger.LogError(message);
                // Don't throw - let the application handle missing configuration gracefully
            }
            
            // Function key is optional but we'll log a warning if it's missing
            if (string.IsNullOrEmpty(_azureFunctionKey))
            {
                _logger.LogWarning("Azure Function Key is not set. Some functionality may be limited.");
            }
            
            // Crypto Reports API key is optional but we'll log a warning if it's missing
            if (string.IsNullOrEmpty(_cryptoReportsApiKey))
            {
                _logger.LogWarning("Crypto Reports API Key is not set. Some functionality may be limited.");
            }
            
            _logger.LogInformation("Configuration validation complete. Bot Token length: {TokenLength}, Azure Function Key present: {HasFunctionKey}, Crypto Reports API Key present: {HasCryptoReportsApiKey}", 
                _botToken?.Length ?? 0,
                !string.IsNullOrEmpty(_azureFunctionKey),
                !string.IsNullOrEmpty(_cryptoReportsApiKey));
        }
    }
}