from azure.identity import DefaultAzureCredential
from azure.keyvault.secrets import SecretClient
from dotenv import load_dotenv
import os

# Load .env file
load_dotenv()

# Check environment
IS_PRODUCTION = os.getenv('AZURE_KEY_VAULT_URL') is not None

if IS_PRODUCTION:
    # Production: Use Azure Key Vault
    VAULT_URL = os.getenv('AZURE_KEY_VAULT_URL')
    credential = DefaultAzureCredential()
    secret_client = SecretClient(vault_url=VAULT_URL, credential=credential)

    def get_secret(secret_name: str) -> str:
        """Retrieve secret from Azure Key Vault."""
        try:
            return secret_client.get_secret(secret_name).value
        except Exception as e:
            print(f"Error retrieving secret {secret_name}: {e}")
            return None
else:
    # Local development: Use .env file
    def get_secret(secret_name: str) -> str:
        """Retrieve secret from .env file."""
        return os.getenv(secret_name)

# Print environment status
print(f"Running in {'production' if IS_PRODUCTION else 'development'} mode")