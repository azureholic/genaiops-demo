using './main.bicep'

param location = 'swedencentral'
param environmentName = 'dev'
param workloadName = 'genaiops'
param owner = 'replace-with-team-name'
param costCenter = 'development'
param logRetentionInDays = 30
param zoneRedundantContainerEnvironment = false
param apiMinReplicas = 1
param apiMaxReplicas = 2
param webMinReplicas = 1
param webMaxReplicas = 2
param workerMinReplicas = 1
param workerMaxReplicas = 1
param modelDeploymentName = 'genaiops-chat'
param modelName = 'gpt-4.1-mini'
param modelVersion = '2025-04-14'
param modelSkuName = 'GlobalStandard'
param modelCapacity = 1
param imageTag = 'dev'
param keyVaultBootstrapSecrets = {}
param tags = {
  dataClassification: 'internal'
  workload: 'genaiops-demo'
}
