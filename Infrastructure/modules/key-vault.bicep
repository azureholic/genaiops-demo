param location string
param name string
param logAnalyticsWorkspaceId string
param applicationInsightsConnectionString string
@secure()
param secretOverrides object
param tags object

resource vault 'Microsoft.KeyVault/vaults@2025-05-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enablePurgeProtection: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    publicNetworkAccess: 'Enabled'
  }
}

resource applicationInsightsSecret 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: vault
  name: 'applicationinsights-connection-string'
  properties: {
    value: applicationInsightsConnectionString
    attributes: {
      enabled: true
    }
  }
}

resource bootstrapSecrets 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = [
  for secret in items(secretOverrides): {
    parent: vault
    name: secret.key
    properties: {
      value: secret.value
      attributes: {
        enabled: true
      }
    }
  }
]

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: vault
  name: 'send-to-log-analytics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

output id string = vault.id
output name string = vault.name
output applicationInsightsSecretUri string = '${vault.properties.vaultUri}secrets/${applicationInsightsSecret.name}'
