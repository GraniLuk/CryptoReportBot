using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CryptoReportBot
{
    public interface IConfigurationManager
    {
        string BotToken { get; }
        string AzureFunctionUrl { get; }
        string? AzureFunctionKey { get; } // Made nullable to handle missing configuration
        string? AllowedUserIds { get; }
    }

    public class ConfigurationManager : IConfigurationManager
    {
        private readonly ILogger<ConfigurationManager> _logger;
        private readonly IConfiguration _configuration;
        private string? _botToken = null;
        private string? _azureFunctionUrl = null;
        private string? _azureFunctionKey = null;
        private string? _allowedUserIds = null;
        private readonly bool _isDevelopment;
        private readonly bool _useEnvironmentVars;

        public ConfigurationManager(ILogger<ConfigurationManager> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" || 
                             Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development";
            _useEnvironmentVars = Environment.GetEnvironmentVariable("USE_ENVIRONMENT_VARIABLES") == "true";
            
            // Debug: Log available configuration providers
            _logger.LogInformation("Configuration providers:");
            foreach (var provider in (_configuration as IConfigurationRoot)?.Providers ?? Array.Empty<IConfigurationProvider>())
            {
                _logger.LogInformation("Provider: {ProviderType}", provider.GetType().Name);
            }
            
            LoadSecrets();
        }

        public string BotToken => _botToken!;
        public string AzureFunctionUrl => _azureFunctionUrl!;
        public string? AzureFunctionKey => _azureFunctionKey; // Can be null now
        public string? AllowedUserIds => _allowedUserIds;

        private void LoadSecrets()
        {
            try
            {
                _logger.LogInformation("Starting to load secrets...");
                _logger.LogInformation("Development mode: {IsDevelopment}, Use Env Vars: {UseEnvVars}", 
                    _isDevelopment, _useEnvironmentVars);
                
                // First check if we should prioritize environment variables (container deployment)
                if (_useEnvironmentVars)
                {
                    _logger.LogInformation("Prioritizing environment variables for configuration");
                    
                    // Get directly from environment variables first
                    _botToken = Environment.GetEnvironmentVariable("alerts_bot_token");
                    _azureFunctionUrl = Environment.GetEnvironmentVariable("azure_function_url");
                    _azureFunctionKey = Environment.GetEnvironmentVariable("azure_function_key");
                    _allowedUserIds = Environment.GetEnvironmentVariable("allowed_user_ids");
                    
                    _logger.LogInformation("Environment variables loaded - Bot token exists: {HasToken}, URL exists: {HasUrl}, Key exists: {HasKey}, Allowed users exists: {HasAllowedUsers}",
                        !string.IsNullOrEmpty(_botToken),
                        !string.IsNullOrEmpty(_azureFunctionUrl),
                        !string.IsNullOrEmpty(_azureFunctionKey),
                        !string.IsNullOrEmpty(_allowedUserIds));
                    
                    // If all required values are set, return early
                    if (!string.IsNullOrEmpty(_botToken) && !string.IsNullOrEmpty(_azureFunctionUrl))
                    {
                        _logger.LogInformation("All required environment variables found, skipping other configuration sources");
                        return;
                    }
                }
                
                // Next, check if we're running in production with Key Vault
                var keyVaultName = Environment.GetEnvironmentVariable("KEY_VAULT_NAME");
                
                if (!string.IsNullOrEmpty(keyVaultName))
                {
                    _logger.LogInformation("Loading secrets from Azure Key Vault: {KeyVaultName}", keyVaultName);
                    var kvUri = $"https://{keyVaultName}.vault.azure.net";
                    
                    var client = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());
                    
                    _botToken = client.GetSecret("alerts_bot_token").Value.Value;
                    _azureFunctionUrl = client.GetSecret("azure_function_url").Value.Value;
                    
                    // Try to get Azure Function Key but don't fail if not found in production
                    try {
                        _azureFunctionKey = client.GetSecret("azure_function_key").Value.Value;
                    }
                    catch (Exception ex) {
                        _logger.LogWarning(ex, "Failed to retrieve azure_function_key from Key Vault - some features may be limited");
                    }
                    
                    // Try to get allowed user IDs but don't fail if not found
                    try {
                        _allowedUserIds = client.GetSecret("allowed_user_ids").Value.Value;
                    }
                    catch (Exception ex) {
                        _logger.LogWarning(ex, "Failed to retrieve allowed_user_ids from Key Vault - all users will be allowed");
                    }
                }
                else if (!_useEnvironmentVars || _isDevelopment)
                {
                    // For development, check User Secrets, local.settings.json, or environment variables
                    _logger.LogInformation("Loading secrets from local configuration");

                    // Try loading user secrets directly as a fallback
                    // This is a workaround if the normal configuration system isn't finding them
                    TryLoadUserSecretsDirectly();

                    // Debug: Try to retrieve token directly and log result
                    var tokenValue = _configuration["alerts_bot_token"];
                    _logger.LogInformation("Direct config access for 'alerts_bot_token': {HasValue}", !string.IsNullOrEmpty(tokenValue));
                    
                    // Only set these values if they're not already set by environment variables
                    _botToken = _botToken ?? tokenValue ?? 
                               Environment.GetEnvironmentVariable("alerts_bot_token");
                    
                    _azureFunctionUrl = _azureFunctionUrl ?? _configuration["azure_function_url"] ?? 
                                       Environment.GetEnvironmentVariable("azure_function_url");
                    
                    _azureFunctionKey = _azureFunctionKey ?? _configuration["azure_function_key"] ?? 
                                       Environment.GetEnvironmentVariable("azure_function_key");
                    
                    _allowedUserIds = _allowedUserIds ?? _configuration["allowed_user_ids"] ?? 
                                     Environment.GetEnvironmentVariable("allowed_user_ids");

                    // Log all available configuration keys for debugging
                    if (_configuration is IConfigurationRoot configRoot)
                    {
                        _logger.LogInformation("Available configuration keys:");
                        foreach (var provider in configRoot.Providers)
                        {
                            if (provider.TryGet("alerts_bot_token", out var value))
                            {
                                _logger.LogInformation("Found 'alerts_bot_token' in provider {ProviderType} with value length: {Length}", 
                                    provider.GetType().Name, value?.Length ?? 0);
                            }
                            
                            if (provider.TryGet("azure_function_key", out var functionKeyValue))
                            {
                                _logger.LogInformation("Found 'azure_function_key' in provider {ProviderType} with value length: {Length}", 
                                    provider.GetType().Name, functionKeyValue?.Length ?? 0);
                            }
                        }
                    }
                }
                
                // Validate with better error messages - only Bot Token and URL are required
                if (string.IsNullOrEmpty(_botToken)) 
                    throw new InvalidOperationException("Bot Token is not set. Make sure 'alerts_bot_token' is available in user secrets or environment variables.");
                
                if (string.IsNullOrEmpty(_azureFunctionUrl)) 
                    throw new InvalidOperationException("Azure Function URL is not set. Make sure 'azure_function_url' is available in user secrets or environment variables.");
                
                // Function key is now optional but we'll log a warning if it's missing
                if (string.IsNullOrEmpty(_azureFunctionKey)) 
                {
                    _logger.LogWarning("Azure Function Key is not set. Some functionality may be limited.");
                }
                
                _logger.LogInformation("Successfully loaded required secrets. Bot Token length: {TokenLength}, Azure Function Key present: {HasFunctionKey}", 
                    _botToken?.Length ?? 0,
                    !string.IsNullOrEmpty(_azureFunctionKey));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load secrets");
                throw;
            }
        }
        
        private void TryLoadUserSecretsDirectly()
        {
            try
            {
                // Try to directly load the user secrets file
                var userSecretsId = "78dfe98c-0b7c-4ef8-a22c-9a998c7ee21f"; // From your .csproj file
                
                // Get the application data path and ensure it's not null or empty
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                
                // In production/container environments, appData might be empty or incorrect
                // Only attempt to load user secrets if we're in development or have a valid path
                if (string.IsNullOrEmpty(appData) && !_isDevelopment)
                {
                    _logger.LogInformation("Skipping user secrets in production environment without valid ApplicationData path");
                    return;
                }
                
                // Ensure we have a valid base path
                var basePath = string.IsNullOrEmpty(appData) ? 
                    Path.Combine(Directory.GetCurrentDirectory(), "Microsoft", "UserSecrets") : 
                    Path.Combine(appData, "Microsoft", "UserSecrets");
                
                var secretsFilePath = Path.Combine(basePath, userSecretsId, "secrets.json");
                
                _logger.LogInformation("Attempting to load secrets directly from: {Path}", secretsFilePath);
                
                if (File.Exists(secretsFilePath))
                {
                    _logger.LogInformation("Found secrets.json file");
                    
                    // Create a new configuration builder just for this file
                    var secretsConfig = new ConfigurationBuilder()
                        .AddJsonFile(secretsFilePath, optional: true)
                        .Build();
                    
                    // Only set these values if they're not already set
                    _botToken = _botToken ?? secretsConfig["alerts_bot_token"];
                    _azureFunctionUrl = _azureFunctionUrl ?? secretsConfig["azure_function_url"];
                    _azureFunctionKey = _azureFunctionKey ?? secretsConfig["azure_function_key"];
                    _allowedUserIds = _allowedUserIds ?? secretsConfig["allowed_user_ids"];
                    
                    _logger.LogInformation("Direct secrets loading - Bot token exists: {HasToken}", 
                        !string.IsNullOrEmpty(_botToken));
                }
                else
                {
                    _logger.LogWarning("User secrets file not found at expected location");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load user secrets directly");
            }
        }
    }
}