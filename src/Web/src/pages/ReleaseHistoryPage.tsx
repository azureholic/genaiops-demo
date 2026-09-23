import {
  Badge,
  Button,
  Card,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Skeleton,
  SkeletonItem,
  Text,
} from '@fluentui/react-components'
import { ArrowClockwiseRegular, ArrowUndoRegular } from '@fluentui/react-icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { ApiError, api } from '../api/client'
import { queryKeys, releasesQuery, versionsQuery } from '../api/queries'
import type { AgentAssignment, ReleaseRecord } from '../api/types'
import ReleaseActionDialog from '../components/ReleaseActionDialog'

export default function ReleaseHistoryPage() {
  const client = useQueryClient()
  const versions = useQuery(versionsQuery)
  const releases = useQuery(releasesQuery)
  const [confirmRollback, setConfirmRollback] = useState(false)
  const [rollbackError, setRollbackError] = useState<ApiError | null>(null)
  const rollbackTarget = useMemo(
    () => findRollbackTarget(releases.data?.releases ?? [], versions.data?.production ?? null),
    [releases.data?.releases, versions.data?.production],
  )

  const rollback = useMutation({
    mutationFn: () => api.rollback(
      versions.data!.eTag,
      'dashboard-user',
      crypto.randomUUID(),
    ),
    onSuccess: async () => {
      setConfirmRollback(false)
      setRollbackError(null)
      await Promise.all([
        client.invalidateQueries({ queryKey: queryKeys.versions }),
        client.invalidateQueries({ queryKey: queryKeys.releases }),
        client.invalidateQueries({ queryKey: queryKeys.metrics }),
      ])
    },
    onError: async (error) => {
      setRollbackError(error instanceof ApiError ? error : new ApiError('Rollback failed.', 0))
      if (error instanceof ApiError && error.status === 412) {
        await Promise.all([
          client.invalidateQueries({ queryKey: queryKeys.versions }),
          client.invalidateQueries({ queryKey: queryKeys.releases }),
        ])
      }
    },
  })

  const refresh = () => void Promise.all([versions.refetch(), releases.refetch()])
  if (versions.isPending || releases.isPending) return <HistorySkeleton />
  if (versions.isError || releases.isError) return <HistoryError onRetry={refresh} />

  return (
    <section aria-labelledby="release-title">
      <div className="page-heading">
        <div>
          <Text as="h1" id="release-title" size={800} weight="semibold">Release History</Text>
          <Text block>Immutable promotion and rollback evidence in newest-first order.</Text>
        </div>
        <div className="heading-actions">
          <Button icon={<ArrowClockwiseRegular />} onClick={refresh}>Refresh</Button>
          <Button
            appearance="primary"
            icon={<ArrowUndoRegular />}
            disabled={!rollbackTarget}
            onClick={() => {
              setRollbackError(null)
              setConfirmRollback(true)
            }}
          >
            Roll back production
          </Button>
        </div>
      </div>
      {!rollbackTarget && versions.data.production && (
        <MessageBar intent="info" className="state-banner">
          <MessageBarBody><MessageBarTitle>Rollback unavailable</MessageBarTitle>No prior production target is present in release evidence.</MessageBarBody>
        </MessageBar>
      )}
      {releases.data.releases.length === 0 ? (
        <div className="empty-state">No promotion or rollback records are available.</div>
      ) : (
        <ol className="release-timeline" aria-label="Release timeline">
          {releases.data.releases.map(release => <ReleaseTimelineItem key={release.id} release={release} />)}
        </ol>
      )}
      {confirmRollback && rollbackTarget && (
        <ReleaseActionDialog
          open
          title="Confirm production rollback"
          actionLabel="Roll back production"
          description={`Production is currently ${versions.data.production?.promptVersion}. This restores the exact prior assignment shown below.`}
          targetVersion={`${rollbackTarget.promptVersion} (${rollbackTarget.agentId})`}
          pending={rollback.isPending}
          error={rollbackError}
          onConfirm={() => rollback.mutate()}
          onClose={() => {
            setConfirmRollback(false)
            setRollbackError(null)
          }}
        />
      )}
    </section>
  )
}

function ReleaseTimelineItem({ release }: { release: ReleaseRecord }) {
  const failed = release.lifecycle === 'Rejected' || release.gateEvidence?.passed === false
  const target = release.operation === 'Rollback' ? release.rollbackTarget : release.newProduction
  return (
    <li>
      <span className={`timeline-marker timeline-marker--${failed ? 'failed' : 'success'}`} aria-hidden="true" />
      <Card className="release-card">
        <div className="release-card__heading">
          <div>
            <Text as="h2" size={500} weight="semibold">
              {release.operation} · {release.promptVersion}
            </Text>
            <Text block size={200}>{formatDate(release.createdAt)} by {release.actor}</Text>
          </div>
          <Badge appearance="filled" color={failed ? 'danger' : 'success'}>{release.lifecycle}</Badge>
        </div>
        <dl className="release-details">
          <div><dt>Agent</dt><dd>{release.agentId}</dd></div>
          <div><dt>From</dt><dd>{assignmentLabel(release.previousProduction)}</dd></div>
          <div><dt>{release.operation === 'Rollback' ? 'Rollback target' : 'To'}</dt><dd>{assignmentLabel(target)}</dd></div>
        </dl>
        {release.gateEvidence && (
          <details className="evidence-details" open={failed}>
            <summary>{failed ? 'Failed quality-gate detail' : 'Quality-gate evidence'}</summary>
            <div className="evidence-grid">
              <Text>Samples: {release.gateEvidence.sampleCount}</Text>
              {Object.entries(release.gateEvidence.observed).map(([metric, value]) => (
                <Text key={metric}>{metric}: {formatMetric(metric, value)}</Text>
              ))}
            </div>
            {release.gateEvidence.reasons.length > 0 && (
              <ul>{release.gateEvidence.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul>
            )}
            <Text block size={200}>Snapshot {release.gateEvidence.metricSnapshotId}</Text>
          </details>
        )}
      </Card>
    </li>
  )
}

function findRollbackTarget(releases: ReleaseRecord[], production: AgentAssignment | null) {
  if (!production) return null
  const latest = [...releases]
    .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
    .find(release =>
      release.newProduction?.promptVersion === production.promptVersion
      && release.newProduction.agentId === production.agentId
      && release.previousProduction)
  return latest?.previousProduction ?? null
}

function assignmentLabel(assignment: AgentAssignment | null) {
  return assignment ? `${assignment.promptVersion} (${assignment.agentId})` : 'None'
}

function formatMetric(name: string, value: number) {
  if (name.toLowerCase().includes('latency') || name === 'sampleCount') return value.toLocaleString()
  return new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 }).format(value)
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function HistorySkeleton() {
  return (
    <section aria-label="Loading release history" className="dashboard-skeleton">
      <Skeleton><SkeletonItem className="skeleton-card" /></Skeleton>
      <Skeleton><SkeletonItem className="skeleton-chart" /></Skeleton>
    </section>
  )
}

function HistoryError({ onRetry }: { onRetry: () => void }) {
  return (
    <section aria-labelledby="release-title">
      <Text as="h1" id="release-title" size={800} weight="semibold">Release History</Text>
      <MessageBar intent="error" role="alert">
        <MessageBarBody><MessageBarTitle>Release history is unavailable</MessageBarTitle>No changes were made.</MessageBarBody>
        <MessageBarActions><Button onClick={onRetry}>Try again</Button></MessageBarActions>
      </MessageBar>
    </section>
  )
}
