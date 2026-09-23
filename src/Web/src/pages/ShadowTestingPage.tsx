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
import { ArrowClockwiseRegular } from '@fluentui/react-icons'
import { useQuery } from '@tanstack/react-query'
import { evaluationsQuery, metricsQuery, versionsQuery } from '../api/queries'
import type { EvaluationSummary } from '../api/types'

const scoreLabels: Record<string, string> = {
  taskAdherence: 'Task adherence',
  groundedness: 'Groundedness',
  toolAccuracy: 'Tool accuracy',
}

export default function ShadowTestingPage() {
  const versions = useQuery(versionsQuery)
  const metrics = useQuery(metricsQuery)
  const evaluations = useQuery(evaluationsQuery)
  const refresh = () => void Promise.all([versions.refetch(), metrics.refetch(), evaluations.refetch()])

  if (versions.isPending || metrics.isPending || evaluations.isPending) {
    return <ShadowSkeleton />
  }
  if (versions.isError || metrics.isError || evaluations.isError) {
    return <ShadowError onRetry={refresh} />
  }

  const candidate = versions.data.candidate
  const records = [...evaluations.data.evaluations]
    .filter(item => !candidate || item.candidatePromptVersion === candidate.promptVersion)
    .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
  const completed = records.filter(item => item.lifecycle === 'Completed')
  const failures = records.filter(item => item.lifecycle === 'Failed' || item.lifecycle === 'Poisoned')
  const delayed = records.filter(isDelayed)
  const partial = completed.filter(isPartial)
  const latencies = completed
    .map(item => item.candidateLatencyMilliseconds)
    .filter((value): value is number => value !== null)
  const averageLatency = latencies.length
    ? Math.round(latencies.reduce((total, value) => total + value, 0) / latencies.length)
    : null
  const hours = records.length > 1
    ? Math.max(1, (Date.parse(records[0].createdAt) - Date.parse(records.at(-1)!.createdAt)) / 3_600_000)
    : 1
  const throughput = records.length ? records.length / hours : 0
  const candidateSnapshot = latestFor(metrics.data.snapshots, candidate?.promptVersion)

  return (
    <section aria-labelledby="shadow-title">
      <div className="page-heading">
        <div>
          <Text as="h1" id="shadow-title" size={800} weight="semibold">Shadow Testing</Text>
          <Text block>Production traffic is compared with the hidden candidate without exposing customer prompts.</Text>
        </div>
        <Button icon={<ArrowClockwiseRegular />} onClick={refresh}>Refresh</Button>
      </div>

      {!candidate && (
        <MessageBar intent="warning" className="state-banner">
          <MessageBarBody>
            <MessageBarTitle>No candidate assigned</MessageBarTitle>
            Shadow comparisons will begin after a candidate assignment.
          </MessageBarBody>
        </MessageBar>
      )}
      {delayed.length > 0 && (
        <MessageBar intent="warning" className="state-banner" role="status">
          <MessageBarBody>
            <MessageBarTitle>{delayed.length} delayed {delayed.length === 1 ? 'evaluation' : 'evaluations'}</MessageBarTitle>
            Pending or running longer than five minutes. Completed evidence remains available below.
          </MessageBarBody>
        </MessageBar>
      )}

      <div className="shadow-summary-grid">
        <Card className="dashboard-card candidate-status-card">
          <CardHeader
            header={<Text as="h2" size={500} weight="semibold">Candidate status</Text>}
            action={
              <Badge appearance="filled" color={candidate ? (failures.length ? 'danger' : 'success') : 'warning'}>
                {candidate ? (failures.length ? 'Attention needed' : 'Active') : 'Unassigned'}
              </Badge>
            }
          />
          {candidate ? (
            <dl className="detail-list">
              <div><dt>Candidate</dt><dd>{candidate.promptVersion} · {candidate.agentId}</dd></div>
              <div><dt>Production</dt><dd>{versions.data.production?.promptVersion ?? 'Unassigned'}</dd></div>
              <div><dt>Samples</dt><dd>{candidateSnapshot?.sampleCount ?? completed.length}</dd></div>
            </dl>
          ) : <Text className="empty-state">No candidate is receiving shadow traffic.</Text>}
        </Card>
        <SummaryCard label="Throughput" value={records.length ? `${throughput.toFixed(1)}/hour` : '—'} detail={`${records.length} comparisons loaded`} />
        <SummaryCard label="Failures" value={String(failures.length)} detail={partial.length ? `${partial.length} partial results` : 'No partial results'} danger={failures.length > 0} />
        <SummaryCard label="Candidate latency" value={averageLatency === null ? '—' : `${averageLatency} ms`} detail={`${latencies.length} measured samples`} />
      </div>

      <Card className="dashboard-card comparison-card">
        <CardHeader
          header={<Text as="h2" size={600} weight="semibold">Recent comparisons</Text>}
          description={<Text>Run references and aggregate scores only. Prompt and response content is hidden by default.</Text>}
        />
        {records.length === 0 ? (
          <Text className="empty-state">No shadow comparisons are available for this candidate.</Text>
        ) : (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Recent shadow comparisons table" aria-describedby="privacy-note">
            <table className="data-table">
              <caption className="visually-hidden">Recent privacy-safe production and candidate comparisons</caption>
              <thead>
                <tr>
                  <th scope="col">Run</th>
                  <th scope="col">Versions</th>
                  <th scope="col">State</th>
                  <th scope="col">Scores</th>
                  <th scope="col">Latency</th>
                  <th scope="col">Started</th>
                </tr>
              </thead>
              <tbody>
                {records.slice(0, 20).map(record => <ComparisonRow key={record.correlationId} record={record} />)}
              </tbody>
            </table>
          </div>
        )}
        <Text id="privacy-note" size={200} className="privacy-note">
          Privacy safeguard: customer input and generated output are not returned by this dashboard API.
        </Text>
      </Card>
    </section>
  )
}

function SummaryCard({ label, value, detail, danger = false }: { label: string; value: string; detail: string; danger?: boolean }) {
  return (
    <Card className={`metric-card shadow-metric${danger ? ' metric-card--danger' : ''}`}>
      <Text size={200}>{label}</Text>
      <Text size={700} weight="semibold">{value}</Text>
      <Text size={200}>{detail}</Text>
    </Card>
  )
}

function ComparisonRow({ record }: { record: EvaluationSummary }) {
  const state = displayState(record)
  const scores = Object.entries(record.scores)
  return (
    <tr>
      <th scope="row"><code>{safeRunReference(record.correlationId)}</code></th>
      <td>{record.productionPromptVersion} → {record.candidatePromptVersion}</td>
      <td>
        <Badge appearance="filled" color={state.color}>{state.label}</Badge>
        {record.attemptCount > 1 && <Text block size={200}>{record.attemptCount} attempts</Text>}
        {(record.lifecycle === 'Failed' || record.lifecycle === 'Poisoned') && record.errorCode && (
          <Text block size={200}>Code: {record.errorCode}</Text>
        )}
      </td>
      <td>
        {scores.length
          ? scores.map(([name, value]) => <Text block size={200} key={name}>{scoreLabels[name] ?? name}: {formatPercent(value)}</Text>)
          : 'Not available'}
      </td>
      <td>{record.candidateLatencyMilliseconds === null ? 'Not measured' : `${record.candidateLatencyMilliseconds} ms`}</td>
      <td>{formatDate(record.createdAt)}</td>
    </tr>
  )
}

function displayState(record: EvaluationSummary): { label: string; color: 'success' | 'warning' | 'danger' | 'informative' } {
  if (record.lifecycle === 'Failed' || record.lifecycle === 'Poisoned') return { label: 'Failed', color: 'danger' }
  if (isDelayed(record)) return { label: 'Delayed', color: 'warning' }
  if (record.lifecycle === 'Completed' && isPartial(record)) return { label: 'Partial', color: 'warning' }
  if (record.lifecycle === 'Completed') return { label: 'Completed', color: 'success' }
  return { label: record.lifecycle, color: 'informative' }
}

function isDelayed(record: EvaluationSummary) {
  return (record.lifecycle === 'Pending' || record.lifecycle === 'Running')
    && Date.now() - Date.parse(record.createdAt) > 5 * 60_000
}

function isPartial(record: EvaluationSummary) {
  return record.lifecycle === 'Completed'
    && (record.candidateLatencyMilliseconds === null || Object.keys(record.scores).length < 3)
}

function latestFor(snapshots: { promptVersion: string; generatedAt: string; sampleCount: number }[], version?: string) {
  return snapshots
    .filter(item => item.promptVersion === version)
    .sort((a, b) => Date.parse(b.generatedAt) - Date.parse(a.generatedAt))[0]
}

function safeRunReference(correlationId: string) {
  const clean = correlationId.replace(/[^a-zA-Z0-9-]/g, '')
  return clean.length <= 12 ? clean : `${clean.slice(0, 8)}…${clean.slice(-4)}`
}

function formatPercent(value: number) {
  return new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 }).format(value)
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function ShadowSkeleton() {
  return (
    <section aria-busy="true" aria-label="Loading shadow testing dashboard">
      <Skeleton className="dashboard-skeleton">
        <SkeletonItem size={32} />
        <SkeletonItem className="skeleton-card" />
        <SkeletonItem className="skeleton-chart" />
      </Skeleton>
    </section>
  )
}

function ShadowError({ onRetry }: { onRetry: () => void }) {
  return (
    <section aria-labelledby="shadow-title">
      <Text as="h1" id="shadow-title" size={800} weight="semibold">Shadow Testing</Text>
      <MessageBar intent="error" role="alert">
        <MessageBarBody>
          <MessageBarTitle>Shadow testing data is unavailable</MessageBarTitle>
          Existing production traffic is unaffected. Check the API and retry.
        </MessageBarBody>
        <MessageBarActions><Button onClick={onRetry}>Try again</Button></MessageBarActions>
      </MessageBar>
    </section>
  )
}
