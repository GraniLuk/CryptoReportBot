targetScope = 'resourceGroup'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@description('Resource group name')
param resourceGroupName string = ''

// Application configuration parameters
@description('Bot token for Telegram bot')
@secure()
param alertsBotToken string = ''

@description('Azure Function URL')
param azureFunctionUrl string = ''

@description('Azure Function key')
@secure()
param azureFunctionKey string = ''

@description('Allowed user IDs for the bot')
param allowedUserIds string = ''

// Create resource token for unique naming
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location, environmentName)
var resourcePrefix = 'cb' // CryptoBot prefix (max 3 chars)

var tags = {
  'azd-env-name': environmentName
}

// Reference existing VNET
resource vnet 'Microsoft.Network/virtualNetworks@2023-04-01' existing = {
  name: 'CryptoVNet'
}

// Get the first subnet for VNET integration (assuming it exists)
var subnetId = '${vnet.id}/subnets/${vnet.properties.subnets[0].name}'

// Log Analytics Workspace
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'az-${resourcePrefix}-law-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'az-${resourcePrefix}-ai-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// User-assigned managed identity
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'az-${resourcePrefix}-mi-${resourceToken}'
  location: location
  tags: tags
}

// App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'az-${resourcePrefix}-asp-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
    size: 'B1'
    family: 'B'
    capacity: 1
  }
  properties: {
    reserved: true // Linux app service plan
    targetWorkerCount: 1
    maximumElasticWorkerCount: 1
  }
}

// App Service (Web App)
resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'az-${resourcePrefix}-app-${resourceToken}'
  location: location
  tags: union(tags, {
    'azd-service-name': 'cryptoreportbot'
  })
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    virtualNetworkSubnetId: subnetId
    vnetRouteAllEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOCKER|cryptoreportcontainer.azurecr.io/cryptotelegram:latest'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      use32BitWorkerProcess: false
      healthCheckPath: '/health'
      cors: {
        allowedOrigins: ['*']
        supportCredentials: false
      }
      appSettings: [
        {
          name: 'USE_ENVIRONMENT_VARIABLES'
          value: 'true'
        }
        {
          name: 'alerts_bot_token'
          value: alertsBotToken
        }
        {
          name: 'azure_function_url'
          value: azureFunctionUrl
        }
        {
          name: 'azure_function_key'
          value: azureFunctionKey
        }
        {
          name: 'allowed_user_ids'
          value: allowedUserIds
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights.properties.ConnectionString
        }
        {
          name: 'ASPNETCORE_URLS'
          value: 'http://+:80'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'APPLICATIONINSIGHTS_ROLE_NAME'
          value: 'CryptoReportBot'
        }
        {
          name: 'DOCKER_REGISTRY_SERVER_URL'
          value: 'https://cryptoreportcontainer.azurecr.io'
        }
        {
          name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
          value: 'false'
        }
      ]
    }
  }
}

// Diagnostic settings for the Web App
resource webAppDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'webAppDiagnostics'
  scope: webApp
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
        retentionPolicy: {
          enabled: false
          days: 0
        }
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
        retentionPolicy: {
          enabled: false
          days: 0
        }
      }
    ]
  }
}

// Outputs
output RESOURCE_GROUP_ID string = resourceGroup().id
output WEB_APP_NAME string = webApp.name
output WEB_APP_URL string = 'https://${webApp.properties.defaultHostName}'
output APPLICATION_INSIGHTS_CONNECTION_STRING string = applicationInsights.properties.ConnectionString
output MANAGED_IDENTITY_CLIENT_ID string = managedIdentity.properties.clientId
output MANAGED_IDENTITY_PRINCIPAL_ID string = managedIdentity.properties.principalId
