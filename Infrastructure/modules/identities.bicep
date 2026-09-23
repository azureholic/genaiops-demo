param location string
param names object
param tags object

resource api 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: names.api
  location: location
  tags: tags
}

resource web 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: names.web
  location: location
  tags: tags
}

resource shadowEvaluator 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: names.shadowEvaluator
  location: location
  tags: tags
}

resource metricsAggregator 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: names.metricsAggregator
  location: location
  tags: tags
}

resource promotionEngine 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: names.promotionEngine
  location: location
  tags: tags
}

resource rollbackEngine 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: names.rollbackEngine
  location: location
  tags: tags
}

output api object = {
  id: api.id
  clientId: api.properties.clientId
  principalId: api.properties.principalId
}
output web object = {
  id: web.id
  clientId: web.properties.clientId
  principalId: web.properties.principalId
}
output shadowEvaluator object = {
  id: shadowEvaluator.id
  clientId: shadowEvaluator.properties.clientId
  principalId: shadowEvaluator.properties.principalId
}
output metricsAggregator object = {
  id: metricsAggregator.id
  clientId: metricsAggregator.properties.clientId
  principalId: metricsAggregator.properties.principalId
}
output promotionEngine object = {
  id: promotionEngine.id
  clientId: promotionEngine.properties.clientId
  principalId: promotionEngine.properties.principalId
}
output rollbackEngine object = {
  id: rollbackEngine.id
  clientId: rollbackEngine.properties.clientId
  principalId: rollbackEngine.properties.principalId
}
