using '../main.bicep'

// Synthetic examples only. Override all identity values before provisioning.
param environmentName = 'production'
param location = 'westeurope'
param entraTenantId = '00000000-0000-0000-0000-000000000000'
param webClientId = '00000000-0000-0000-0000-000000000000'
param sqlAdministratorObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdministratorName = 'REPLACE-WITH-SQL-ADMIN-GROUP'
