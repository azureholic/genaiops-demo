targetScope = 'resourceGroup'

@description('Azure region. Confirm model and service availability before deployment.')
param location string = resourceGroup().location

@minLength(2)
@maxLength(12)
param environmentName string = 'dev'

@minLength(3)
param workloadName string = 'genaiops'
param owner string = 'platform-team'
param costCenter string = 'demo'
param logRetentionInDays int = 30
param zoneRedundantContainerEnvironment bool = false

param apiMinReplicas int = 1
param apiMaxReplicas int = 3
param webMinReplicas int = 1
param webMaxReplicas int = 3
param workerMinReplicas int = 1
param workerMaxReplicas int = 1
@description('Deploy Container Apps after immutable images have been pushed to the registry.')
param deployApplications bool = true

param modelDeploymentName string = 'genaiops-chat'
param modelName string = 'gpt-4.1-mini'
param modelVersion string = '2025-04-14'
@allowed([
  'GlobalStandard'
  'DataZoneStandard'
  'Standard'
])
param modelSkuName string = 'GlobalStandard'
@minValue(1)
param modelCapacity int = 1

param imageTag string = 'dev'
@description('Optional immutable image references (loginServer/repository@sha256:digest). Each supplied value overrides imageTag for that workload.')
param imageReferences object = {}
param imageNames object = {
  api: 'genaiops-api'
  web: 'genaiops-web'
  shadowEvaluator: 'genaiops-shadow-evaluator'
  metricsAggregator: 'genaiops-metrics-aggregator'
  promotionEngine: 'genaiops-promotion-engine'
  rollbackEngine: 'genaiops-rollback-engine'
}

@description('Optional bootstrap secrets. Values are protected in deployment history and stored only in Key Vault.')
@secure()
param keyVaultBootstrapSecrets object = {}

param tags object = {}

var suffix = take(uniqueString(subscription().id, resourceGroup().id, environmentName), 8)
var baseName = toLower('${workloadName}-${environmentName}')
var resourceTags = union(tags, {
  application: workloadName
  environment: environmentName
  owner: owner
  costCenter: costCenter
  managedBy: 'Bicep'
})
var names = {
  logAnalytics: '${baseName}-law-${suffix}'
  applicationInsights: '${baseName}-appi-${suffix}'
  registry: take(replace('${workloadName}${environmentName}${suffix}', '-', ''), 50)
  keyVault: take('${baseName}-kv-${suffix}', 24)
  cosmos: take('${baseName}-cosmos-${suffix}', 44)
  database: 'genaiops'
  foundry: take('${baseName}-ai-${suffix}', 64)
  foundryProject: take('${baseName}-project', 64)
  openAi: take('${baseName}-aoai-${suffix}', 64)
  containerEnvironment: take('${baseName}-cae-${suffix}', 60)
  api: take('${baseName}-api', 32)
  web: take('${baseName}-web', 32)
  shadowEvaluator: take('${baseName}-shadow', 32)
  metricsAggregator: take('${baseName}-metrics', 32)
  promotionEngine: take('${baseName}-promotion', 32)
  rollbackEngine: take('${baseName}-rollback', 32)
}
var identityNames = {
  api: '${names.api}-id'
  web: '${names.web}-id'
  shadowEvaluator: '${names.shadowEvaluator}-id'
  metricsAggregator: '${names.metricsAggregator}-id'
  promotionEngine: '${names.promotionEngine}-id'
  rollbackEngine: '${names.rollbackEngine}-id'
}

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    location: location
    logAnalyticsName: names.logAnalytics
    applicationInsightsName: names.applicationInsights
    retentionInDays: logRetentionInDays
    tags: resourceTags
  }
}

module identities 'modules/identities.bicep' = {
  name: 'identities'
  params: {
    location: location
    names: identityNames
    tags: resourceTags
  }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    location: location
    name: names.registry
    logAnalyticsWorkspaceId: observability.outputs.workspaceId
    tags: resourceTags
  }
}

var workloadIdentities = [
  identities.outputs.api
  identities.outputs.shadowEvaluator
  identities.outputs.metricsAggregator
  identities.outputs.promotionEngine
  identities.outputs.rollbackEngine
]
module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    location: location
    name: names.keyVault
    logAnalyticsWorkspaceId: observability.outputs.workspaceId
    applicationInsightsConnectionString: observability.outputs.applicationInsightsConnectionString
    secretOverrides: keyVaultBootstrapSecrets
    tags: resourceTags
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    location: location
    accountName: names.cosmos
    databaseName: names.database
    containerNames: [
      'registry'
      'deployments'
      'evaluations'
      'metrics'
      'experiments'
      'requests'
      'shadow-work'
    ]
    principalIds: map(workloadIdentities, identity => identity.principalId)
    logAnalyticsWorkspaceId: observability.outputs.workspaceId
    tags: resourceTags
  }
}

module ai 'modules/ai.bicep' = {
  name: 'ai'
  params: {
    location: location
    foundryAccountName: names.foundry
    foundryProjectName: names.foundryProject
    openAiAccountName: names.openAi
    modelDeploymentName: modelDeploymentName
    modelName: modelName
    modelVersion: modelVersion
    modelSkuName: modelSkuName
    modelCapacity: modelCapacity
    principalIds: map(workloadIdentities, identity => identity.principalId)
    logAnalyticsWorkspaceId: observability.outputs.workspaceId
    tags: resourceTags
  }
}

module containerEnvironment 'modules/container-environment.bicep' = {
  name: 'container-environment'
  params: {
    location: location
    name: names.containerEnvironment
    logAnalyticsWorkspaceId: observability.outputs.workspaceId
    zoneRedundant: zoneRedundantContainerEnvironment
    tags: resourceTags
  }
}

var acrPullRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)
resource acrPullAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for (identityName, index) in items(identityNames): {
    name: guid(resourceGroup().id, names.registry, identityName.value, acrPullRoleId)
    scope: registryResource
    properties: {
      principalId: [
        identities.outputs.api.principalId
        identities.outputs.metricsAggregator.principalId
        identities.outputs.promotionEngine.principalId
        identities.outputs.rollbackEngine.principalId
        identities.outputs.shadowEvaluator.principalId
        identities.outputs.web.principalId
      ][index]
      principalType: 'ServicePrincipal'
      roleDefinitionId: acrPullRoleId
    }
    dependsOn: [
      registry
    ]
  }
]

var keyVaultSecretsUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)
resource registryResource 'Microsoft.ContainerRegistry/registries@2025-11-01' existing = {
  #disable-next-line BCP334 // The deterministic eight-character suffix guarantees the minimum.
  name: names.registry
}

resource keyVaultResource 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: names.keyVault
}

resource applicationInsightsResource 'Microsoft.Insights/components@2020-02-02' existing = {
  name: names.applicationInsights
}

resource keyVaultAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for (identityName, index) in [
    identityNames.api
    identityNames.shadowEvaluator
    identityNames.metricsAggregator
    identityNames.promotionEngine
    identityNames.rollbackEngine
  ]: {
    name: guid(resourceGroup().id, names.keyVault, identityName, keyVaultSecretsUserRoleId)
    scope: keyVaultResource
    properties: {
      principalId: [
        identities.outputs.api.principalId
        identities.outputs.shadowEvaluator.principalId
        identities.outputs.metricsAggregator.principalId
        identities.outputs.promotionEngine.principalId
        identities.outputs.rollbackEngine.principalId
      ][index]
      principalType: 'ServicePrincipal'
      roleDefinitionId: keyVaultSecretsUserRoleId
    }
    dependsOn: [
      keyVault
    ]
  }
]

var monitoringMetricsPublisherRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '3913510d-42f4-4e42-8a64-420c390055eb'
)
resource telemetryPublishers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for (identityName, index) in [
    identityNames.api
    identityNames.shadowEvaluator
    identityNames.metricsAggregator
    identityNames.promotionEngine
    identityNames.rollbackEngine
  ]: {
    name: guid(resourceGroup().id, names.applicationInsights, identityName, monitoringMetricsPublisherRoleId)
    scope: applicationInsightsResource
    properties: {
      principalId: [
        identities.outputs.api.principalId
        identities.outputs.shadowEvaluator.principalId
        identities.outputs.metricsAggregator.principalId
        identities.outputs.promotionEngine.principalId
        identities.outputs.rollbackEngine.principalId
      ][index]
      principalType: 'ServicePrincipal'
      roleDefinitionId: monitoringMetricsPublisherRoleId
    }
    dependsOn: [
      observability
    ]
  }
]

var telemetrySecret = [
  {
    name: 'applicationinsights-connection-string'
    keyVaultUrl: keyVault.outputs.applicationInsightsSecretUri
    identity: identities.outputs.api.id
  }
]
var workloadImages = {
  api: imageReferences.?api ?? '${registry.outputs.loginServer}/${imageNames.api}:${imageTag}'
  web: imageReferences.?web ?? '${registry.outputs.loginServer}/${imageNames.web}:${imageTag}'
  shadowEvaluator: imageReferences.?shadowEvaluator ?? '${registry.outputs.loginServer}/${imageNames.shadowEvaluator}:${imageTag}'
  metricsAggregator: imageReferences.?metricsAggregator ?? '${registry.outputs.loginServer}/${imageNames.metricsAggregator}:${imageTag}'
  promotionEngine: imageReferences.?promotionEngine ?? '${registry.outputs.loginServer}/${imageNames.promotionEngine}:${imageTag}'
  rollbackEngine: imageReferences.?rollbackEngine ?? '${registry.outputs.loginServer}/${imageNames.rollbackEngine}:${imageTag}'
}
var commonDotnetEnvironment = [
  {
    name: 'DOTNET_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'Persistence__Provider'
    value: 'Cosmos'
  }
  {
    name: 'Persistence__Cosmos__Endpoint'
    value: cosmos.outputs.endpoint
  }
  {
    name: 'Persistence__Cosmos__DatabaseName'
    value: cosmos.outputs.databaseName
  }
  {
    name: 'Chat__Provider'
    value: 'Foundry'
  }
  {
    name: 'Chat__Foundry__ProjectEndpoint'
    value: ai.outputs.projectEndpoint
  }
  {
    name: 'Shadow__EvaluationProvider'
    value: 'Foundry'
  }
  {
    name: 'Shadow__Foundry__EvaluationEndpoint'
    value: ai.outputs.projectEndpoint
  }
  {
    name: 'ContinuousEvaluation__Provider'
    value: 'Foundry'
  }
  {
    name: 'Observability__AzureMonitor__Enabled'
    value: 'true'
  }
  {
    name: 'Observability__AzureMonitor__ConnectionString'
    secretRef: 'applicationinsights-connection-string'
  }
]

module api 'modules/container-app.bicep' = if (deployApplications) {
  name: 'api'
  dependsOn: [
    acrPullAssignments
    keyVaultAssignments
    telemetryPublishers
  ]
  params: {
    location: location
    name: names.api
    environmentId: containerEnvironment.outputs.id
    identity: identities.outputs.api
    registryServer: registry.outputs.loginServer
    image: workloadImages.api
    cpu: '0.5'
    memory: '1Gi'
    minReplicas: apiMinReplicas
    maxReplicas: apiMaxReplicas
    env: concat(commonDotnetEnvironment, [
      {
        name: 'AZURE_CLIENT_ID'
        value: identities.outputs.api.clientId
      }
      {
        name: 'Chat__Foundry__ManagedIdentityClientId'
        value: identities.outputs.api.clientId
      }
      {
        name: 'Shadow__Foundry__ManagedIdentityClientId'
        value: identities.outputs.api.clientId
      }
    ])
    keyVaultSecrets: telemetrySecret
    externalIngress: true
    enableHttpProbes: true
    tags: resourceTags
  }
}

module web 'modules/container-app.bicep' = if (deployApplications) {
  name: 'web'
  dependsOn: [
    acrPullAssignments
  ]
  params: {
    location: location
    name: names.web
    environmentId: containerEnvironment.outputs.id
    identity: identities.outputs.web
    registryServer: registry.outputs.loginServer
    image: workloadImages.web
    cpu: '0.25'
    memory: '0.5Gi'
    minReplicas: webMinReplicas
    maxReplicas: webMaxReplicas
    env: [
      {
        name: 'API_HOST'
        value: 'https://${api!.outputs.fqdn}'
      }
    ]
    externalIngress: true
    enableHttpProbes: true
    tags: resourceTags
  }
}

module shadowEvaluator 'modules/container-app.bicep' = if (deployApplications) {
  name: 'shadow-evaluator'
  dependsOn: [
    acrPullAssignments
    keyVaultAssignments
    telemetryPublishers
  ]
  params: {
    location: location
    name: names.shadowEvaluator
    environmentId: containerEnvironment.outputs.id
    identity: identities.outputs.shadowEvaluator
    registryServer: registry.outputs.loginServer
    image: workloadImages.shadowEvaluator
    cpu: '0.25'
    memory: '0.5Gi'
    minReplicas: workerMinReplicas
    maxReplicas: workerMaxReplicas
    env: concat(commonDotnetEnvironment, [
      {
        name: 'AZURE_CLIENT_ID'
        value: identities.outputs.shadowEvaluator.clientId
      }
      {
        name: 'Chat__Foundry__ManagedIdentityClientId'
        value: identities.outputs.shadowEvaluator.clientId
      }
      {
        name: 'Shadow__Foundry__ManagedIdentityClientId'
        value: identities.outputs.shadowEvaluator.clientId
      }
    ])
    keyVaultSecrets: [
      {
        name: 'applicationinsights-connection-string'
        keyVaultUrl: keyVault.outputs.applicationInsightsSecretUri
        identity: identities.outputs.shadowEvaluator.id
      }
    ]
    tags: resourceTags
  }
}

module metricsAggregator 'modules/container-app.bicep' = if (deployApplications) {
  name: 'metrics-aggregator'
  dependsOn: [
    acrPullAssignments
    keyVaultAssignments
    telemetryPublishers
  ]
  params: {
    location: location
    name: names.metricsAggregator
    environmentId: containerEnvironment.outputs.id
    identity: identities.outputs.metricsAggregator
    registryServer: registry.outputs.loginServer
    image: workloadImages.metricsAggregator
    cpu: '0.25'
    memory: '0.5Gi'
    minReplicas: workerMinReplicas
    maxReplicas: workerMaxReplicas
    env: concat(commonDotnetEnvironment, [
      {
        name: 'AZURE_CLIENT_ID'
        value: identities.outputs.metricsAggregator.clientId
      }
      {
        name: 'Chat__Foundry__ManagedIdentityClientId'
        value: identities.outputs.metricsAggregator.clientId
      }
      {
        name: 'Shadow__Foundry__ManagedIdentityClientId'
        value: identities.outputs.metricsAggregator.clientId
      }
      {
        name: 'ContinuousEvaluation__DatasetPath'
        value: '/app/EvaluationData'
      }
    ])
    keyVaultSecrets: [
      {
        name: 'applicationinsights-connection-string'
        keyVaultUrl: keyVault.outputs.applicationInsightsSecretUri
        identity: identities.outputs.metricsAggregator.id
      }
    ]
    tags: resourceTags
  }
}

module promotionEngine 'modules/container-app.bicep' = if (deployApplications) {
  name: 'promotion-engine'
  dependsOn: [
    acrPullAssignments
    keyVaultAssignments
    telemetryPublishers
  ]
  params: {
    location: location
    name: names.promotionEngine
    environmentId: containerEnvironment.outputs.id
    identity: identities.outputs.promotionEngine
    registryServer: registry.outputs.loginServer
    image: workloadImages.promotionEngine
    cpu: '0.25'
    memory: '0.5Gi'
    minReplicas: workerMinReplicas
    maxReplicas: workerMaxReplicas
    env: concat(commonDotnetEnvironment, [
      {
        name: 'AZURE_CLIENT_ID'
        value: identities.outputs.promotionEngine.clientId
      }
      {
        name: 'Promotion__Enabled'
        value: 'false'
      }
    ])
    keyVaultSecrets: [
      {
        name: 'applicationinsights-connection-string'
        keyVaultUrl: keyVault.outputs.applicationInsightsSecretUri
        identity: identities.outputs.promotionEngine.id
      }
    ]
    tags: resourceTags
  }
}

module rollbackEngine 'modules/container-app.bicep' = if (deployApplications) {
  name: 'rollback-engine'
  dependsOn: [
    acrPullAssignments
    keyVaultAssignments
    telemetryPublishers
  ]
  params: {
    location: location
    name: names.rollbackEngine
    environmentId: containerEnvironment.outputs.id
    identity: identities.outputs.rollbackEngine
    registryServer: registry.outputs.loginServer
    image: workloadImages.rollbackEngine
    cpu: '0.25'
    memory: '0.5Gi'
    minReplicas: workerMinReplicas
    maxReplicas: workerMaxReplicas
    env: concat(commonDotnetEnvironment, [
      {
        name: 'AZURE_CLIENT_ID'
        value: identities.outputs.rollbackEngine.clientId
      }
      {
        name: 'Rollback__Enabled'
        value: 'false'
      }
    ])
    keyVaultSecrets: [
      {
        name: 'applicationinsights-connection-string'
        keyVaultUrl: keyVault.outputs.applicationInsightsSecretUri
        identity: identities.outputs.rollbackEngine.id
      }
    ]
    tags: resourceTags
  }
}

output webUrl string = deployApplications ? 'https://${web!.outputs.fqdn}' : ''
output apiUrl string = deployApplications ? 'https://${api!.outputs.fqdn}' : ''
output containerRegistryName string = names.registry
output containerRegistryLoginServer string = registry.outputs.loginServer
output containerAppsEnvironmentId string = containerEnvironment.outputs.id
output cosmosAccountId string = cosmos.outputs.accountId
output foundryProjectId string = ai.outputs.projectId
output foundryProjectEndpoint string = ai.outputs.projectEndpoint
output openAiAccountId string = ai.outputs.openAiAccountId
output modelDeploymentName string = ai.outputs.modelDeploymentName
output keyVaultName string = keyVault.outputs.name
output applicationInsightsId string = observability.outputs.applicationInsightsId
output workloadIdentityClientIds object = {
  api: identities.outputs.api.clientId
  web: identities.outputs.web.clientId
  shadowEvaluator: identities.outputs.shadowEvaluator.clientId
  metricsAggregator: identities.outputs.metricsAggregator.clientId
  promotionEngine: identities.outputs.promotionEngine.clientId
  rollbackEngine: identities.outputs.rollbackEngine.clientId
}
