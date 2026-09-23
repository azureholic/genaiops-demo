import {
  Badge,
  Button,
  Card,
  CardHeader,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Skeleton,
  SkeletonItem,
  Text,
} from '@fluentui/react-components'
import { ArrowClockwiseRegular, ArrowUploadRegular } from '@fluentui/react-icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError, api } from '../api/client'
import { metricsQuery, queryKeys, releasesQuery, versionsQuery } from '../api/queries'
import type { MetricSnapshot, PromptVersion, ReleaseGateEvidence } from '../api/types'
import ReleaseActionDialog from '../components/ReleaseActionDialog'
import { useRealtime } from '../realtime/RealtimeProvider'

const metricNames = ['taskAdherence', 'groundedness', 'toolAccuracy'] as const
const metricLabels = {
  taskAdherence: 'Task adherence',
  groundedness: 'Groundedness',
  toolAccuracy: 'Tool accuracy',
} as const

type RegistryStatus = 'production' | 'candidate' | 'experiment' | 'historical'

export default function PromptRegistryPage() {
  const client = useQueryClient()
  const versions = useQuery(versionsQuery)
  const metrics = useQuery(metricsQuery)
  const releases = useQuery(releasesQuery)
  const realtime = useRealtime()
  const [confirmVersion, setConfirmVersion] = useState<string | null>(null)
  const [promotionError, setPromotionError] = useState<ApiError | null>(null)

  const promotion = useMutation({
    mutationFn: (version: string) => api.promote(
      version,
      versions.data!.eTag,
      'dashboard-user',
      crypto.randomUUID(),
    ),
    onSuccess: async () => {
      setConfirmVersion(null)
      setPromotionError(null)
      await Promise.all([
        client.invalidateQueries({ queryKey: queryKeys.versions }),
        client.invalidateQueries({ queryKey: queryKeys.metrics }),
        client.invalidateQueries({ queryKey: queryKeys.releases }),
      ])
    },
    onError: async (error) => {
      setPromotionError(error instanceof ApiError ? error : new ApiError('Promotion failed.', 0))
      await client.invalidateQueries({ queryKey: queryKeys.releases })
      if (error instanceof ApiError && error.status === 412) {
        await client.invalidateQueries({ queryKey: queryKeys.versions })
      }
    },
  })

  const refresh = () => void Promise.all([versions.refetch(), metrics.refetch(), releases.refetch()])
  if (versions.isPending || metrics.isPending || releases.isPending) {
    return <RegistrySkeleton />
  }
  if (versions.isError || metrics.isError || releases.isError) {
    return <RegistryError onRetry={refresh} />
  }

  const latestMetrics = latestSnapshots(metrics.data.snapshots)
  const failedGates = latestFailedGates(releases.data.releases)
  const experimentVersions = new Set(realtime.activeExperiment?.allocations.map(item => item.promptVersion) ?? [])
  const candidate = versions.data.candidate?.promptVersion
  const stale = versions.isStale || metrics.isStale || releases.isStale

  return (
    <section aria-labelledby="registry-title">
      <div className="page-heading">
        <div>
          <Text as="h1" id="registry-title" size={800} weight="semibold">Prompt Registry</Text>
          <Text block>Version metadata, release status, and expected versus observed quality.</Text>
        </div>
        <Button icon={<ArrowClockwiseRegular />} onClick={refresh}>
          {stale ? 'Refresh stale data' : 'Refresh'}
        </Button>
      </div>
      {stale && (
        <MessageBar intent="warning" className="state-banner">
          <MessageBarBody><MessageBarTitle>Showing cached registry data</MessageBarTitle>Refresh before changing production.</MessageBarBody>
        </MessageBar>
      )}
      {versions.data.versions.length === 0 ? (
        <div className="empty-state">No prompt versions have been registered.</div>
      ) : (
        <div className="registry-grid">
          {[...versions.data.versions]
            .sort((a, b) => a.version.localeCompare(b.version, undefined, { numeric: true }))
            .map((version) => {
              const status = getStatus(version, versions.data.production?.promptVersion, candidate, experimentVersions)
              return (
                <VersionCard
                  key={version.id}
                  version={version}
                  status={status}
                  metrics={latestMetrics.get(version.version)}
                  failedGate={failedGates.get(version.version)}
                  canPromote={status === 'candidate'}
                  onPromote={() => {
                    setPromotionError(null)
                    setConfirmVersion(version.version)
                  }}
                />
              )
            })}
        </div>
      )}
      {confirmVersion && (
        <ReleaseActionDialog
          open
          title="Confirm production promotion"
          actionLabel="Promote to production"
          description="This updates live routing only if the current quality gates pass. A rejected gate will remain visibly failed."
          targetVersion={confirmVersion}
          pending={promotion.isPending}
          error={promotionError}
          onConfirm={() => promotion.mutate(confirmVersion)}
          onClose={() => {
            setConfirmVersion(null)
            setPromotionError(null)
          }}
        />
      )}
    </section>
  )
}

function VersionCard({
  version,
  status,
  metrics,
  failedGate,
  canPromote,
  onPromote,
}: {
  version: PromptVersion
  status: RegistryStatus
  metrics?: MetricSnapshot
  failedGate?: ReleaseGateEvidence
  canPromote: boolean
  onPromote: () => void
}) {
  const badgeColor = status === 'production'
    ? 'success'
    : status === 'candidate'
      ? 'warning'
      : status === 'experiment'
        ? 'informative'
        : 'subtle'

  return (
    <Card className={`registry-card registry-card--${status}`}>
      <CardHeader
        header={<Text as="h2" size={600} weight="semibold">{version.version} · {version.displayName}</Text>}
        action={<Badge appearance="filled" color={badgeColor}>{status}</Badge>}
      />
      <Text size={200}>Registered {formatDate(version.createdAt)} · {version.lifecycle}</Text>
      <div className="metric-comparison" role="table" aria-label={`${version.version} expected and observed metrics`}>
        <div role="row" className="metric-comparison__header">
          <span role="columnheader">Metric</span>
          <span role="columnheader">Expected</span>
          <span role="columnheader">Observed</span>
        </div>
        {metricNames.map((name) => (
          <div role="row" key={name}>
            <span role="cell">{metricLabels[name]}</span>
            <span role="cell">{formatPercent(version.expectedMetrics[name])}</span>
            <span role="cell">{metrics ? formatPercent(metrics.metrics[name]) : 'Not measured'}</span>
          </div>
        ))}
      </div>
      {failedGate && (
        <MessageBar intent="error" className="gate-failure" role="alert">
          <MessageBarBody>
            <MessageBarTitle>Promotion blocked by quality gates</MessageBarTitle>
            <ul>
              {failedGate.reasons.map(reason => <li key={reason}>{reason}</li>)}
            </ul>
          </MessageBarBody>
        </MessageBar>
      )}
      {canPromote && (
        <Button appearance="primary" icon={<ArrowUploadRegular />} onClick={onPromote}>
          Promote {version.version}
        </Button>
      )}
    </Card>
  )
}

function RegistrySkeleton() {
  return (
    <section aria-label="Loading prompt registry" className="dashboard-skeleton">
      <Skeleton><SkeletonItem className="skeleton-card" /></Skeleton>
      <Skeleton><SkeletonItem className="skeleton-chart" /></Skeleton>
    </section>
  )
}

function RegistryError({ onRetry }: { onRetry: () => void }) {
  return (
    <section aria-labelledby="registry-title">
      <Text as="h1" id="registry-title" size={800} weight="semibold">Prompt Registry</Text>
      <MessageBar intent="error" role="alert">
        <MessageBarBody><MessageBarTitle>Registry data is unavailable</MessageBarTitle>No changes were made.</MessageBarBody>
        <MessageBarActions><Button onClick={onRetry}>Try again</Button></MessageBarActions>
      </MessageBar>
    </section>
  )
}

function latestSnapshots(snapshots: MetricSnapshot[]) {
  const result = new Map<string, MetricSnapshot>()
  for (const snapshot of snapshots) {
    const current = result.get(snapshot.promptVersion)
    if (!current || Date.parse(snapshot.generatedAt) > Date.parse(current.generatedAt)) {
      result.set(snapshot.promptVersion, snapshot)
    }
  }
  return result
}

function latestFailedGates(releases: { promptVersion: string; createdAt: string; gateEvidence: ReleaseGateEvidence | null }[]) {
  const result = new Map<string, ReleaseGateEvidence>()
  for (const release of [...releases].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))) {
    if (!result.has(release.promptVersion) && release.gateEvidence && !release.gateEvidence.passed) {
      result.set(release.promptVersion, release.gateEvidence)
    }
  }
  return result
}

function getStatus(
  version: PromptVersion,
  production: string | undefined,
  candidate: string | undefined,
  experiments: Set<string>,
): RegistryStatus {
  if (version.version === production) return 'production'
  if (version.version === candidate) return 'candidate'
  if (experiments.has(version.version)) return 'experiment'
  return 'historical'
}

function formatPercent(value?: number) {
  if (value === undefined) return 'Not set'
  const normalized = value > 1 ? value / 100 : value
  return new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 }).format(normalized)
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}
