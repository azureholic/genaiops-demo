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
import { useMemo, useState } from 'react'
import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { evaluationsQuery, metricsQuery, releasesQuery, versionsQuery } from '../api/queries'
import type { EvaluationLifecycle, MetricSnapshot, ReleaseGateEvidence } from '../api/types'

const metricNames = ['taskAdherence', 'groundedness', 'toolAccuracy'] as const
const metricLabels: Record<string, string> = {
  taskAdherence: 'Task adherence',
  groundedness: 'Groundedness',
  toolAccuracy: 'Tool accuracy',
}
const chartColors = ['var(--cp-accent)', 'var(--cp-success)', 'var(--cp-link)'] as const

type WindowFilter = '24h' | '7d' | 'all'
type StateFilter = 'All' | EvaluationLifecycle

export default function EvaluationDashboardPage() {
  const versions = useQuery(versionsQuery)
  const metrics = useQuery(metricsQuery)
  const evaluations = useQuery(evaluationsQuery)
  const releases = useQuery(releasesQuery)
  const [versionFilter, setVersionFilter] = useState('all')
  const [stateFilter, setStateFilter] = useState<StateFilter>('All')
  const [windowFilter, setWindowFilter] = useState<WindowFilter>('all')
  const refresh = () => void Promise.all([
    versions.refetch(),
    metrics.refetch(),
    evaluations.refetch(),
    releases.refetch(),
  ])

  const filteredEvaluations = useMemo(() => evaluations.data?.evaluations.filter(item =>
    (versionFilter === 'all' || item.candidatePromptVersion === versionFilter)
    && (stateFilter === 'All' || item.lifecycle === stateFilter)
    && inWindow(item.createdAt, windowFilter)) ?? [], [evaluations.data, stateFilter, versionFilter, windowFilter])
  const filteredSnapshots = useMemo(() => metrics.data?.snapshots.filter(item =>
    (versionFilter === 'all' || item.promptVersion === versionFilter)
    && inWindow(item.windowEnd, windowFilter)) ?? [], [metrics.data, versionFilter, windowFilter])

  if (versions.isPending || metrics.isPending || evaluations.isPending || releases.isPending) {
    return <EvaluationSkeleton />
  }
  if (versions.isError || metrics.isError || evaluations.isError || releases.isError) {
    return <EvaluationError onRetry={refresh} />
  }

  const completed = filteredEvaluations.filter(item => item.lifecycle === 'Completed')
  const failed = filteredEvaluations.filter(item => item.lifecycle === 'Failed' || item.lifecycle === 'Poisoned')
  const running = filteredEvaluations.filter(item => item.lifecycle === 'Pending' || item.lifecycle === 'Running')
  const latest = latestSnapshots(filteredSnapshots)
  const productionVersion = versions.data.production?.promptVersion
  const candidateVersion = versions.data.candidate?.promptVersion
  const [production, candidate] = matchedSnapshots(filteredSnapshots, productionVersion, candidateVersion)
  const v1 = latest.get('v1')
  const v2 = latest.get('v2')
  const v3 = latest.get('v3')
  const latestGates = latestGateEvidence(releases.data.releases)
  const v3Gate = latestGates.get('v3')
  const sampleCount = filteredSnapshots.reduce((total, snapshot) => total + snapshot.sampleCount, 0)

  return (
    <section aria-labelledby="evaluation-title">
      <div className="page-heading">
        <div>
          <Text as="h1" id="evaluation-title" size={800} weight="semibold">Evaluation Dashboard</Text>
          <Text block>Quality trends, sample coverage, and release gates for every prompt version.</Text>
        </div>
        <Button icon={<ArrowClockwiseRegular />} onClick={refresh}>Refresh</Button>
      </div>

      <form className="filter-bar" aria-label="Evaluation filters" onSubmit={event => event.preventDefault()}>
        <label>
          Prompt version
          <select value={versionFilter} onChange={event => setVersionFilter(event.target.value)}>
            <option value="all">All versions</option>
            {versions.data.versions.map(version => <option key={version.id} value={version.version}>{version.version}</option>)}
          </select>
        </label>
        <label>
          Evaluation state
          <select value={stateFilter} onChange={event => setStateFilter(event.target.value as StateFilter)}>
            {['All', 'Pending', 'Running', 'Completed', 'Failed', 'Poisoned'].map(state => <option key={state}>{state}</option>)}
          </select>
        </label>
        <label>
          Time window
          <select value={windowFilter} onChange={event => setWindowFilter(event.target.value as WindowFilter)}>
            <option value="24h">Last 24 hours</option>
            <option value="7d">Last 7 days</option>
            <option value="all">All available</option>
          </select>
        </label>
        <Text className="filter-count" aria-live="polite">{filteredEvaluations.length} evaluation records</Text>
      </form>

      {v2 && v1 && meanQuality(v2) > meanQuality(v1) && (
        <MessageBar intent="success" className="state-banner">
          <MessageBarBody>
            <MessageBarTitle>v2 improvement confirmed</MessageBarTitle>
            Mean quality improved by {formatPoints(meanQuality(v2) - meanQuality(v1))} over v1 on observed snapshots.
          </MessageBarBody>
        </MessageBar>
      )}
      {v3 && v2 && meanQuality(v3) < meanQuality(v2) && (
        <MessageBar intent="error" className="state-banner regression-banner" role="alert">
          <MessageBarBody>
            <MessageBarTitle>v3 regression detected</MessageBarTitle>
            Mean quality dropped {formatPoints(meanQuality(v2) - meanQuality(v3))} from v2.
            {v3Gate?.passed === false ? ' Promotion quality gates failed.' : ' Review before promotion.'}
          </MessageBarBody>
        </MessageBar>
      )}

      <div className="evaluation-metric-grid">
        {metricNames.map(name => (
          <MetricComparisonCard
            key={name}
            label={metricLabels[name]}
            production={production?.metrics[name]}
            candidate={candidate?.metrics[name]}
          />
        ))}
        <Card className="metric-card">
          <Text size={200}>Sample coverage</Text>
          <Text size={700} weight="semibold">{sampleCount.toLocaleString()}</Text>
          <Text size={200}>{completed.length} complete · {running.length} pending/running · {failed.length} failed</Text>
        </Card>
      </div>

      <div className="evaluation-layout">
        <Card className="dashboard-card evaluation-trend-card">
          <CardHeader
            header={<Text as="h2" size={600} weight="semibold">Metric trends</Text>}
            description={<Text>Observed scores by evaluation window. The table is an equivalent view of the chart.</Text>}
          />
          {filteredSnapshots.length ? (
            <>
              <MetricTrendChart snapshots={filteredSnapshots} />
              <details className="chart-table-details">
                <summary>View accessible trend data table</summary>
                <TrendTable snapshots={filteredSnapshots} />
              </details>
            </>
          ) : <Text className="empty-state">No metric snapshots match these filters.</Text>}
        </Card>

        <Card className="dashboard-card gate-card">
          <CardHeader header={<Text as="h2" size={600} weight="semibold">Quality gates</Text>} />
          {latestGates.size ? (
            <ul className="gate-list">
              {[...latestGates.entries()].map(([version, gate]) => (
                <li key={version} className={gate.passed ? 'gate-item--passed' : 'gate-item--failed'}>
                  <div>
                    <Text weight="semibold">{version}</Text>
                    <Badge appearance="filled" color={gate.passed ? 'success' : 'danger'}>
                      {gate.passed ? 'Passed' : 'Failed'}
                    </Badge>
                  </div>
                  <Text size={200}>{gate.sampleCount} samples · {formatDateRange(gate.windowStart, gate.windowEnd)}</Text>
                  {!gate.passed && gate.reasons.length > 0 && (
                    <ul>{gate.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul>
                  )}
                </li>
              ))}
            </ul>
          ) : <Text className="empty-state">No release quality-gate evidence is available.</Text>}
        </Card>
      </div>

      <Card className="dashboard-card state-card">
        <CardHeader header={<Text as="h2" size={600} weight="semibold">Evaluation states</Text>} />
        {filteredEvaluations.length ? (
          <div className="state-summary" role="list" aria-label="Evaluation state summary">
            {(['Pending', 'Running', 'Completed', 'Failed', 'Poisoned'] as EvaluationLifecycle[]).map(state => {
              const count = filteredEvaluations.filter(item => item.lifecycle === state).length
              return (
                <div role="listitem" key={state}>
                  <Text weight="semibold">{state}</Text>
                  <Text>{count}</Text>
                </div>
              )
            })}
          </div>
        ) : <Text className="empty-state">No evaluations match these filters.</Text>}
      </Card>
    </section>
  )
}

function MetricComparisonCard({ label, production, candidate }: { label: string; production?: number; candidate?: number }) {
  const delta = production !== undefined && candidate !== undefined ? candidate - production : undefined
  return (
    <Card className={`metric-card${delta !== undefined && delta < 0 ? ' metric-card--danger' : ''}`}>
      <Text size={200}>{label}</Text>
      <Text size={700} weight="semibold">{candidate === undefined ? '—' : formatPercent(candidate)}</Text>
      <Text size={200}>Production {production === undefined ? 'not measured' : formatPercent(production)}</Text>
      <Text size={200}>
        {delta === undefined ? 'Comparison unavailable' : `${delta >= 0 ? '+' : ''}${formatPoints(delta)} candidate delta`}
      </Text>
    </Card>
  )
}

function MetricTrendChart({ snapshots }: { snapshots: MetricSnapshot[] }) {
  const data = chartData(snapshots)
  return (
    <div className="chart-container" role="img" aria-label={trendSummary(snapshots)}>
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={data} margin={{ top: 12, right: 16, bottom: 8, left: 0 }}>
          <CartesianGrid stroke="var(--cp-border)" strokeDasharray="4 4" />
          <XAxis dataKey="label" tick={{ fill: 'var(--cp-text-muted)' }} />
          <YAxis domain={[0, 1]} tickFormatter={(value: number) => `${Math.round(value * 100)}%`} tick={{ fill: 'var(--cp-text-muted)' }} />
          <Tooltip formatter={value => formatPercent(Number(value))} contentStyle={{ background: 'var(--cp-surface)', borderColor: 'var(--cp-border)', color: 'var(--cp-text)' }} />
          <Legend />
          {metricNames.map((name, index) => (
            <Line key={name} name={metricLabels[name]} type="monotone" dataKey={name} stroke={chartColors[index]} strokeWidth={2} />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}

function TrendTable({ snapshots }: { snapshots: MetricSnapshot[] }) {
  const sorted = [...snapshots].sort((a, b) => Date.parse(a.windowEnd) - Date.parse(b.windowEnd))
  return (
    <div className="table-scroll" tabIndex={0} role="region" aria-label="Metric trend data">
      <table className="data-table compact-table">
        <thead>
          <tr>
            <th scope="col">Window ending</th>
            <th scope="col">Version</th>
            <th scope="col">Samples</th>
            {metricNames.map(name => <th key={name} scope="col">{metricLabels[name]}</th>)}
          </tr>
        </thead>
        <tbody>
          {sorted.map(snapshot => (
            <tr key={snapshot.id}>
              <th scope="row">{formatDate(snapshot.windowEnd)}</th>
              <td>{snapshot.promptVersion}</td>
              <td>{snapshot.sampleCount}</td>
              {metricNames.map(name => <td key={name}>{formatPercent(snapshot.metrics[name])}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function chartData(snapshots: MetricSnapshot[]) {
  return [...snapshots]
    .sort((a, b) => Date.parse(a.windowEnd) - Date.parse(b.windowEnd))
    .slice(-20)
    .map(snapshot => ({
      label: `${snapshot.promptVersion} · ${new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(new Date(snapshot.windowEnd))}`,
      ...snapshot.metrics,
    }))
}

function trendSummary(snapshots: MetricSnapshot[]) {
  const latest = [...snapshots].sort((a, b) => Date.parse(b.windowEnd) - Date.parse(a.windowEnd))[0]
  return `Line chart of ${snapshots.length} metric snapshots. Latest is ${latest.promptVersion} with task adherence ${formatPercent(latest.metrics.taskAdherence)}, groundedness ${formatPercent(latest.metrics.groundedness)}, and tool accuracy ${formatPercent(latest.metrics.toolAccuracy)}.`
}

function latestSnapshots(snapshots: MetricSnapshot[]) {
  const result = new Map<string, MetricSnapshot>()
  for (const snapshot of snapshots) {
    const current = result.get(snapshot.promptVersion)
    if (!current || Date.parse(snapshot.generatedAt) > Date.parse(current.generatedAt)) result.set(snapshot.promptVersion, snapshot)
  }
  return result
}

function matchedSnapshots(snapshots: MetricSnapshot[], productionVersion?: string, candidateVersion?: string) {
  if (!productionVersion || !candidateVersion) return [undefined, undefined] as const
  const candidates = snapshots
    .filter(snapshot => snapshot.promptVersion === candidateVersion)
    .sort((a, b) => Date.parse(b.generatedAt) - Date.parse(a.generatedAt))
  for (const candidate of candidates) {
    const production = snapshots.find(snapshot =>
      snapshot.promptVersion === productionVersion
      && snapshot.windowStart === candidate.windowStart
      && snapshot.windowEnd === candidate.windowEnd)
    if (production) return [production, candidate] as const
  }
  return [undefined, undefined] as const
}

function latestGateEvidence(releases: { promptVersion: string; createdAt: string; gateEvidence: ReleaseGateEvidence | null }[]) {
  const result = new Map<string, ReleaseGateEvidence>()
  for (const release of [...releases].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))) {
    if (release.gateEvidence && !result.has(release.promptVersion)) result.set(release.promptVersion, release.gateEvidence)
  }
  return result
}

function inWindow(value: string, windowFilter: WindowFilter) {
  if (windowFilter === 'all') return true
  const hours = windowFilter === '24h' ? 24 : 24 * 7
  return Date.now() - Date.parse(value) <= hours * 3_600_000
}

function meanQuality(snapshot: MetricSnapshot) {
  const values = metricNames.map(name => snapshot.metrics[name]).filter((value): value is number => value !== undefined)
  return values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : 0
}

function formatPercent(value: number | undefined) {
  return value === undefined ? '—' : new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 }).format(value)
}

function formatPoints(value: number) {
  return `${(value * 100).toFixed(1)} points`
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function formatDateRange(start: string, end: string) {
  const formatter = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' })
  return `${formatter.format(new Date(start))}–${formatter.format(new Date(end))}`
}

function EvaluationSkeleton() {
  return (
    <section aria-busy="true" aria-label="Loading evaluation dashboard">
      <Skeleton className="dashboard-skeleton">
        <SkeletonItem size={32} />
        <SkeletonItem className="skeleton-card" />
        <SkeletonItem className="skeleton-chart" />
      </Skeleton>
    </section>
  )
}

function EvaluationError({ onRetry }: { onRetry: () => void }) {
  return (
    <section aria-labelledby="evaluation-title">
      <Text as="h1" id="evaluation-title" size={800} weight="semibold">Evaluation Dashboard</Text>
      <MessageBar intent="error" role="alert">
        <MessageBarBody>
          <MessageBarTitle>Evaluation data is unavailable</MessageBarTitle>
          Quality evidence could not be loaded. Do not make release decisions until it is refreshed.
        </MessageBarBody>
        <MessageBarActions><Button onClick={onRetry}>Try again</Button></MessageBarActions>
      </MessageBar>
    </section>
  )
}
