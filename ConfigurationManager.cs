using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
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
            LoadSecrets();
        }

        public string BotToken => _botToken;
        public string AzureFunctionUrl => _azureFunctionUrl;
        public string AzureFunctionKey => _azureFunctionKey;

        private void LoadSecrets()
        {
            try
            {
                // First, check if we're running in production with Key Vault
                var keyVaultName = Environment.GetEnvironmentVariable("KEY_VAULT_NAME");
                
                if (!string.IsNullOrEmpty(keyVaultName))
                {
                    _logger.LogInformation("Loading secrets from Azure Key Vault: {KeyVaultName}", keyVaultName);
                    var kvUri = $"https://{keyVaultName}.vault.azure.net";
                    
                    var client = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());
                    
                    _botToken = client.GetSecret("alerts-bot-token").Value.Value;
                    _azureFunctionUrl = client.GetSecret("azure-function-url").Value.Value;
                    _azureFunctionKey = client.GetSecret("azure-function-key").Value.Value;
                }
                else
                {
                    // For development, check User Secrets, local.settings.json, or environment variables
                    _logger.LogInformation("Loading secrets from local configuration");
                    
                    _botToken = _configuration["alerts-bot-token"] ?? 
                               Environment.GetEnvironmentVariable("alerts-bot-token");
                    
                    _azureFunctionUrl = _configuration["azure-function-url"] ?? 
                                       Environment.GetEnvironmentVariable("azure-function-url");
                    
                    _azureFunctionKey = _configuration["azure-function-key"] ?? 
                                       Environment.GetEnvironmentVariable("azure-function-key");
                }
                
                // Validate
                if (string.IsNullOrEmpty(_botToken)) 
                    throw new InvalidOperationException("Bot Token is not set");
                if (string.IsNullOrEmpty(_azureFunctionUrl)) 
                    throw new InvalidOperationException("Azure Function URL is not set");
                if (string.IsNullOrEmpty(_azureFunctionKey)) 
                    throw new InvalidOperationException("Azure Function Key is not set");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load secrets");
                throw;
            }
        }
    }
}
