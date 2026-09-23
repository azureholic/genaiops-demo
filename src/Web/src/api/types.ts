export interface AgentAssignment {
  agentId: string
  promptVersion: string
  slot: 'Production' | 'Candidate'
  assignedAt: string
  unassignedAt?: string | null
}

export interface PromptVersion {
  id: string
  version: string
  displayName: string
  lifecycle: string
  createdAt: string
  expectedMetrics: Record<string, number>
}

export interface VersionsResponse {
  registryId: string
  eTag: string
  production: AgentAssignment | null
  candidate: AgentAssignment | null
  versions: PromptVersion[]
  continuationToken: string | null
}

export interface MetricSnapshot {
  id: string
  promptVersion: string
  windowStart: string
  windowEnd: string
  generatedAt: string
  sampleCount: number
  successfulCount: number
  failureCount: number
  metrics: Record<string, number>
}

export interface MetricsResponse {
  registryId: string
  snapshots: MetricSnapshot[]
  continuationToken: string | null
}

export interface MetricsUpdated extends Omit<MetricSnapshot, 'id'> {}

export interface EvaluationUpdated {
  productionPromptVersion: string
  candidatePromptVersion: string
  lifecycle: string
  scores: Record<string, number>
  latencyMilliseconds: number | null
  completedAt: string
}

export interface ExperimentAllocation {
  promptVersion: string
  percentage: number
}

export interface ExperimentUpdated {
  lifecycle: string
  allocations: ExperimentAllocation[]
  updatedAt: string
}

export interface ReleaseUpdated {
  operation: string
  promptVersion: string
  lifecycle: string
  occurredAt: string
}
