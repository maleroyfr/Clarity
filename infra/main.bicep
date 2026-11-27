targetScope = 'resourceGroup'

@description('Short lowercase environment name, such as dev or production.')
@minLength(2)
@maxLength(12)
param environmentName string
@description('Azure region selected by the operator.')
param location string
@description('Tenant hosting the application registration and SQL identities.')
param entraTenantId string
@description('Clarity OIDC application registration client ID, not a deployment identity.')
param webClientId string
@description('Entra group object ID responsible for SQL administration.')
param sqlAdministratorObjectId string
param sqlAdministratorName string
@description('Existing client credential secret name to populate privately in the new vault.')
param webClientSecretName string = 'clarity-oidc'

var suffix = uniqueString(resourceGroup().id, environmentName)
var prefix = 'clarity-${environmentName}'
var tags = { application: 'Clarity', environment: environmentName }

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${prefix}-app'
  location: location
  tags: tags
}

resource egressIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: '${prefix}-egress'
  location: location
  tags: tags
  sku: { name: 'Standard' }
  properties: { publicIPAllocationMethod: 'Static' }
}

resource nat 'Microsoft.Network/natGateways@2024-05-01' = {
  name: '${prefix}-nat'
  location: location
  tags: tags
  sku: { name: 'Standard' }
  properties: {
    publicIpAddresses: [{ id: egressIp.id }]
    idleTimeoutInMinutes: 10
  }
}

resource network 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${prefix}-network'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.42.0.0/16'] }
    subnets: [
      {
        name: 'app'
        properties: {
          addressPrefix: '10.42.0.0/24'
          natGateway: { id: nat.id }
          delegations: [{ name: 'web', properties: { serviceName: 'Microsoft.Web/serverFarms' } }]
        }
      }
      {
        name: 'endpoints'
        properties: {
          addressPrefix: '10.42.1.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: 'runners'
        properties: {
          addressPrefix: '10.42.2.0/24'
          defaultOutboundAccess: false
          natGateway: { id: nat.id }
        }
      }
    ]
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-insights'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    IngestionMode: 'LogAnalytics'
  }
}

resource sql 'Microsoft.Sql/servers@2023-08-01' = {
  name: '${prefix}-${suffix}'
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: sqlAdministratorName
      sid: sqlAdministratorObjectId
      tenantId: entraTenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sql
  name: 'Clarity'
  location: location
  tags: tags
  sku: { name: 'S0', tier: 'Standard' }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource encryption 'Microsoft.Sql/servers/databases/transparentDataEncryption@2023-08-01' = {
  parent: database
  name: 'current'
  properties: { state: 'Enabled' }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'cl${suffix}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Disabled'
    networkAcls: { defaultAction: 'Deny', bypass: 'None' }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: { enabled: true, days: 7 }
    containerDeleteRetentionPolicy: { enabled: true, days: 7 }
  }
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for name in ['raw-artifacts', 'exports', 'script-packages']: {
  parent: blobService
  name: name
  properties: { publicAccess: 'None' }
}]

// Retention and project deletion turn current raw/export files into previous versions. Previous versions are aged from their original
// write, so this rule removes them at the next daily policy run (at least 7 days after upload); soft delete then keeps them 7 more days.
// Without it, versioning would keep every "deleted" file indefinitely.
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'expire-deleted-file-versions'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['raw-artifacts/', 'exports/'] }
            actions: { version: { delete: { daysAfterCreationGreaterThan: 7 } } }
          }
        }
      ]
    }
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'cl-${suffix}-kv'
  location: location
  tags: tags
  properties: {
    tenantId: entraTenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Disabled'
    networkAcls: { defaultAction: 'Deny', bypass: 'None' }
  }
}

module sqlEndpoint 'modules/private-endpoint.bicep' = {
  name: 'sql-endpoint'
  params: {
    name: '${prefix}-sql'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', network.name, 'endpoints')
    virtualNetworkId: network.id
    resourceId: sql.id
    groupId: 'sqlServer'
    privateDnsZoneName: 'privatelink${environment().suffixes.sqlServerHostname}'
  }
}

module blobEndpoint 'modules/private-endpoint.bicep' = {
  name: 'blob-endpoint'
  params: {
    name: '${prefix}-blob'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', network.name, 'endpoints')
    virtualNetworkId: network.id
    resourceId: storage.id
    groupId: 'blob'
    privateDnsZoneName: 'privatelink.blob.${environment().suffixes.storage}'
  }
}

module vaultEndpoint 'modules/private-endpoint.bicep' = {
  name: 'vault-endpoint'
  params: {
    name: '${prefix}-vault'
    location: location
    subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', network.name, 'endpoints')
    virtualNetworkId: network.id
    resourceId: vault.id
    groupId: 'vault'
    privateDnsZoneName: 'privatelink${replace(environment().suffixes.keyvaultDns, '.vault.', '.vaultcore.')}'
  }
}

var blobContributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
resource blobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, blobContributorRole)
  scope: storage
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: blobContributorRole }
}

var secretReaderRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
resource vaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, identity.id, secretReaderRole)
  scope: vault
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: secretReaderRole }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${prefix}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: 'B1', capacity: 1 }
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: '${prefix}-${suffix}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    keyVaultReferenceIdentity: identity.id
    virtualNetworkSubnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', network.name, 'app')
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'chmod +x /home/site/wwwroot/Clarity.Web && /home/site/wwwroot/Clarity.Web'
      alwaysOn: true
      http20Enabled: true
      webSocketsEnabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      vnetRouteAllEnabled: true
      healthCheckPath: '/health/ready'
      scmIpSecurityRestrictionsDefaultAction: 'Deny'
      scmIpSecurityRestrictions: [
        { name: 'PrivateRunnerEgress', ipAddress: '${egressIp.properties.ipAddress}/32', action: 'Allow', priority: 100 }
      ]
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'ASPNETCORE_URLS', value: 'http://0.0.0.0:8080' }
        { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
        { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
        { name: 'AZURE_TOKEN_CREDENTIALS', value: 'ManagedIdentityCredential' }
        { name: 'ConnectionStrings__Clarity', value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;TrustServerCertificate=False;' }
        { name: 'Storage__BlobServiceUri', value: storage.properties.primaryEndpoints.blob }
        { name: 'AzureAd__Instance', value: environment().authentication.loginEndpoint }
        { name: 'AzureAd__TenantId', value: entraTenantId }
        { name: 'AzureAd__ClientId', value: webClientId }
        { name: 'AzureAd__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=${webClientSecretName})' }
        { name: 'AzureAd__CallbackPath', value: '/signin-oidc' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: 'disabled' }
        { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'false' }
      ]
    }
  }
}

resource scmAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'scm'
  properties: { allow: false }
}

resource ftpAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'ftp'
  properties: { allow: false }
}

output webAppName string = app.name
output webAppUrl string = 'https://${app.properties.defaultHostName}'
output sqlServerName string = sql.name
output sqlHost string = sql.properties.fullyQualifiedDomainName
output databaseName string = database.name
output vaultName string = vault.name
output appIdentityClientId string = identity.properties.clientId
output appIdentityObjectId string = identity.properties.principalId
output runnerSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', network.name, 'runners')
