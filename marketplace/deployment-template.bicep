targetScope = 'resourceGroup'

@description('Deployment location for all Claim Settlement resources.')
param location string = resourceGroup().location

@description('Customer environment name, such as production.')
param environmentName string = 'production'

@description('SQL administrator login name.')
param sqlAdminLogin string

@secure()
@description('SQL administrator password. Supply this as a secure deployment parameter or Key Vault reference.')
param sqlAdminPassword string

@description('Unique Azure SQL server name.')
param sqlServerName string

@description('Claim Settlement database name.')
param sqlDatabaseName string = 'claimsettlement-db'

@description('Globally unique storage account name.')
param storageAccountName string

@description('Globally unique Azure OpenAI account name.')
param openAiAccountName string

@description('Globally unique Azure AI Search service name.')
param searchServiceName string

@description('Application Insights resource name.')
param appInsightsName string

@description('Log Analytics workspace name.')
param logAnalyticsName string

module claimSettlement '../infra/azure/main.bicep' = {
  name: 'claimSettlement-${environmentName}'
  params: {
    location: location
    environmentName: environmentName
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    storageAccountName: storageAccountName
    openAiAccountName: openAiAccountName
    searchServiceName: searchServiceName
    appInsightsName: appInsightsName
    logAnalyticsName: logAnalyticsName
  }
}

output sqlServerFqdn string = claimSettlement.outputs.sqlServerFqdn
output storageAccountId string = claimSettlement.outputs.storageAccountId
output openAiEndpoint string = claimSettlement.outputs.openAiEndpoint
output applicationInsightsConnectionString string = claimSettlement.outputs.applicationInsightsConnectionString
output azureSearchName string = claimSettlement.outputs.azureSearchName
