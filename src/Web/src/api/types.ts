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

export type EvaluationLifecycle = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Poisoned'

export interface EvaluationSummary {
  correlationId: string
  productionAgentId: string
  productionPromptVersion: string
  candidateAgentId: string
  candidatePromptVersion: string
  lifecycle: EvaluationLifecycle
  scores: Record<string, number>
  candidateLatencyMilliseconds: number | null
  attemptCount: number
  errorCode: string | null
  errorMessage: string | null
  createdAt: string
  completedAt: string | null
}

export interface EvaluationsResponse {
  registryId: string
  evaluations: EvaluationSummary[]
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

export interface ReleaseGateEvidence {
  metricSnapshotId: string
  windowStart: string
  windowEnd: string
  sampleCount: number
  observed: Record<string, number>
  thresholds: Record<string, number>
  passed: boolean
  reasons: string[]
}

export interface ReleaseRecord {
  id: string
  registryId: string
  operation: 'Promotion' | 'Rollback'
  promptVersion: string
  agentId: string
  lifecycle: 'Created' | 'Promoted' | 'Rejected' | 'RolledBack' | 'Superseded'
  actor: string
  createdAt: string
  previousProduction: AgentAssignment | null
  newProduction: AgentAssignment | null
  rollbackTarget: AgentAssignment | null
  gateEvidence: ReleaseGateEvidence | null
}

export interface ReleasesResponse {
  registryId: string
  releases: ReleaseRecord[]
  continuationToken: string | null
}

export interface ReleaseMutationResponse {
  registryId: string
  eTag: string
  production: AgentAssignment | null
  release: ReleaseRecord
  replayed: boolean
}

export interface ProblemDetails {
  title?: string
  detail?: string
  code?: string
  correlationId?: string
  gateEvidence?: ReleaseGateEvidence
}
