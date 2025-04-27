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
        string AzureFunctionKey { get; }
    }

    public class ConfigurationManager : IConfigurationManager
    {
        private readonly ILogger<ConfigurationManager> _logger;
        private readonly IConfiguration _configuration;
        private string _botToken;
        private string _azureFunctionUrl;
        private string _azureFunctionKey;

        public ConfigurationManager(ILogger<ConfigurationManager> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Debug: Log available configuration providers
            _logger.LogInformation("Configuration providers:");
            foreach (var provider in (_configuration as IConfigurationRoot)?.Providers ?? Array.Empty<IConfigurationProvider>())
            {
                _logger.LogInformation("Provider: {ProviderType}", provider.GetType().Name);
            }
            
            LoadSecrets();
        }

        public string BotToken => _botToken;
        public string AzureFunctionUrl => _azureFunctionUrl;
        public string AzureFunctionKey => _azureFunctionKey;

        private void LoadSecrets()
        {
            try
            {
                _logger.LogInformation("Starting to load secrets...");
                
                // First, check if we're running in production with Key Vault
                var keyVaultName = Environment.GetEnvironmentVariable("KEY_VAULT_NAME");
                
                if (!string.IsNullOrEmpty(keyVaultName))
                {
                    _logger.LogInformation("Loading secrets from Azure Key Vault: {KeyVaultName}", keyVaultName);
                    var kvUri = $"https://{keyVaultName}.vault.azure.net";
                    
                    var client = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());
                    
                    _botToken = client.GetSecret("alerts_bot_token").Value.Value;
                    _azureFunctionUrl = client.GetSecret("azure_function_url").Value.Value;
                    _azureFunctionKey = client.GetSecret("azure_function_key").Value.Value;
                }
                else
                {
                    // For development, check User Secrets, local.settings.json, or environment variables
                    _logger.LogInformation("Loading secrets from local configuration");

                    // Try loading user secrets directly as a fallback
                    // This is a workaround if the normal configuration system isn't finding them
                    TryLoadUserSecretsDirectly();

                    // Debug: Try to retrieve token directly and log result
                    var tokenValue = _configuration["alerts_bot_token"];
                    _logger.LogInformation("Direct config access for 'alerts_bot_token': {HasValue}", !string.IsNullOrEmpty(tokenValue));
                    
                    _botToken = tokenValue ?? 
                               Environment.GetEnvironmentVariable("alerts_bot_token") ??
                               _botToken; // Use the one loaded directly if available
                    
                    _azureFunctionUrl = _configuration["azure_function_url"] ?? 
                                       Environment.GetEnvironmentVariable("azure_function_url") ??
                                       _azureFunctionUrl;
                    
                    _azureFunctionKey = _configuration["azure_function_key"] ?? 
                                       Environment.GetEnvironmentVariable("azure_function_key") ??
                                       _azureFunctionKey;

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
                        }
                    }
                }
                
                // Validate with better error messages
                if (string.IsNullOrEmpty(_botToken)) 
                    throw new InvalidOperationException("Bot Token is not set. Make sure 'alerts_bot_token' is available in user secrets or environment variables.");
                if (string.IsNullOrEmpty(_azureFunctionUrl)) 
                    throw new InvalidOperationException("Azure Function URL is not set. Make sure 'azure_function_url' is available in user secrets or environment variables.");
                if (string.IsNullOrEmpty(_azureFunctionKey)) 
                    throw new InvalidOperationException("Azure Function Key is not set. Make sure 'azure_function_key' is available in user secrets or environment variables.");
                
                _logger.LogInformation("Successfully loaded all required secrets");
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
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var secretsFilePath = Path.Combine(appData, "Microsoft", "UserSecrets", userSecretsId, "secrets.json");
                
                _logger.LogInformation("Attempting to load secrets directly from: {Path}", secretsFilePath);
                
                if (File.Exists(secretsFilePath))
                {
                    _logger.LogInformation("Found secrets.json file");
                    
                    // Create a new configuration builder just for this file
                    var secretsConfig = new ConfigurationBuilder()
                        .AddJsonFile(secretsFilePath, optional: true)
                        .Build();
                    
                    _botToken = secretsConfig["alerts_bot_token"];
                    _azureFunctionUrl = secretsConfig["azure_function_url"];
                    _azureFunctionKey = secretsConfig["azure_function_key"];
                    
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