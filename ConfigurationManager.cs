using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
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
        private string _botToken;
        private string _azureFunctionUrl;
        private string _azureFunctionKey;

        public ConfigurationManager(ILogger<ConfigurationManager> logger)
        {
            _logger = logger;
            LoadSecrets();
        }

        public string BotToken => _botToken;
        public string AzureFunctionUrl => _azureFunctionUrl;
        public string AzureFunctionKey => _azureFunctionKey;

        private void LoadSecrets()
        {
            try
            {
                // In a real scenario, you might use different approaches:
                // 1. Azure Key Vault (similar to Python version)
                // 2. .NET User Secrets for development
                // 3. Environment variables
                // 4. appsettings.json with proper encryption

                // For Azure Key Vault approach:
                var keyVaultName = Environment.GetEnvironmentVariable("KEY_VAULT_NAME");
                var kvUri = $"https://{keyVaultName}.vault.azure.net";
                
                var client = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());
                
                _botToken = client.GetSecret("alerts-bot-token").Value.Value;
                _azureFunctionUrl = client.GetSecret("azure-function-url").Value.Value;
                _azureFunctionKey = client.GetSecret("azure-function-key").Value.Value;
                
                // Validate
                if (string.IsNullOrEmpty(_botToken)) 
                    throw new InvalidOperationException("BOT_TOKEN is not set");
                if (string.IsNullOrEmpty(_azureFunctionUrl)) 
                    throw new InvalidOperationException("AZURE_FUNCTION_URL is not set");
                if (string.IsNullOrEmpty(_azureFunctionKey)) 
                    throw new InvalidOperationException("AZURE_FUNCTION_KEY is not set");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load secrets");
                throw;
            }
        }
    }
}
