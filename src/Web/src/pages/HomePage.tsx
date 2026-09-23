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
import { metricsQuery, versionsQuery } from '../api/queries'
import type { AgentAssignment, MetricSnapshot } from '../api/types'
import { useRealtime } from '../realtime/RealtimeProvider'

const metricNames = ['taskAdherence', 'groundedness', 'toolAccuracy'] as const
const metricLabels: Record<(typeof metricNames)[number], string> = {
  taskAdherence: 'Task adherence',
  groundedness: 'Groundedness',
  toolAccuracy: 'Tool accuracy',
}

export default function HomePage() {
  const versions = useQuery(versionsQuery)
  const metrics = useQuery(metricsQuery)
  const realtime = useRealtime()
  const isLoading = versions.isPending || metrics.isPending
  const hasError = versions.isError || metrics.isError

  const refresh = () => {
    void Promise.all([versions.refetch(), metrics.refetch()])
  }

  if (isLoading) return <DashboardSkeleton />

  if (hasError) {
    return (
      <section aria-labelledby="home-title">
        <PageHeading stale={false} onRefresh={refresh} />
        <MessageBar intent="error" role="alert">
          <MessageBarBody>
            <MessageBarTitle>Dashboard data is unavailable</MessageBarTitle>
            Check that the API is running, then try again.
          </MessageBarBody>
          <MessageBarActions>
            <Button onClick={refresh}>Try again</Button>
          </MessageBarActions>
        </MessageBar>
      </section>
    )
  }

  const snapshots = metrics.data?.snapshots ?? []
  const latestByVersion = latestSnapshots(snapshots)
  const latest = versions.data?.production
    ? latestByVersion.get(versions.data.production.promptVersion)
    : undefined
  const stale = versions.isStale || metrics.isStale

  return (
    <section aria-labelledby="home-title">
      <PageHeading stale={stale} onRefresh={refresh} />
      {stale && (
        <MessageBar intent="warning" className="state-banner">
          <MessageBarBody>
            <MessageBarTitle>Showing cached data</MessageBarTitle>
            The last update may be older than 30 seconds.
          </MessageBarBody>
          <MessageBarActions><Button onClick={refresh}>Refresh</Button></MessageBarActions>
        </MessageBar>
      )}

      <div className="assignment-grid">
        <AssignmentCard title="Production" assignment={versions.data?.production ?? null} />
        <AssignmentCard title="Candidate" assignment={versions.data?.candidate ?? null} />
      </div>

      <section aria-labelledby="metrics-title">
        <Text as="h2" id="metrics-title" size={600} weight="semibold">Current quality</Text>
        {latest ? <MetricCards snapshot={latest} /> : <EmptyState message="No production metrics are available yet." />}
      </section>

      <div className="dashboard-grid">
        <Card className="dashboard-card trend-card">
          <CardHeader header={<Text as="h2" size={600} weight="semibold">Quality trends</Text>} />
          {snapshots.length > 0 ? <QualityChart snapshots={snapshots} /> : <EmptyState message="Trends appear after the first evaluation window." />}
        </Card>

        <Card className="dashboard-card">
          <CardHeader header={<Text as="h2" size={600} weight="semibold">Active experiment</Text>} />
          {realtime.activeExperiment ? (
            <>
              <Badge appearance="filled" color="informative">{realtime.activeExperiment.lifecycle}</Badge>
              <ul className="data-list">
                {realtime.activeExperiment.allocations.map((allocation) => (
                  <li key={allocation.promptVersion}>
                    <Text weight="semibold">{allocation.promptVersion}</Text>
                    <Text>{allocation.percentage}% traffic</Text>
                  </li>
                ))}
              </ul>
            </>
          ) : <EmptyState message="No active experiment reported in this session." />}
        </Card>

        <Card className="dashboard-card">
          <CardHeader header={<Text as="h2" size={600} weight="semibold">Recent releases</Text>} />
          {realtime.recentReleases.length > 0 ? (
            <ol className="data-list">
              {realtime.recentReleases.map((release, index) => (
                <li key={`${release.occurredAt}-${index}`}>
                  <Text weight="semibold">{release.operation}: {release.promptVersion}</Text>
                  <Text>{release.lifecycle} · {formatDate(release.occurredAt)}</Text>
                </li>
              ))}
            </ol>
          ) : (
            versions.data?.production
              ? <ReleaseFromAssignment assignment={versions.data.production} />
              : <EmptyState message="No releases have been reported." />
          )}
        </Card>
      </div>
    </section>
  )
}

function PageHeading({ stale, onRefresh }: { stale: boolean; onRefresh: () => void }) {
  return (
    <div className="page-heading">
      <div>
        <Text as="h1" id="home-title" size={800} weight="semibold">Operations overview</Text>
        <Text block>Production health, candidate readiness, and quality signals.</Text>
      </div>
      <Button icon={<ArrowClockwiseRegular />} onClick={onRefresh}>
        {stale ? 'Refresh stale data' : 'Refresh'}
      </Button>
    </div>
  )
}

function AssignmentCard({ title, assignment }: { title: string; assignment: AgentAssignment | null }) {
  return (
    <Card className="assignment-card">
      <CardHeader
        header={<Text as="h2" size={500} weight="semibold">{title}</Text>}
        action={<Badge color={assignment ? 'success' : 'warning'}>{assignment ? 'Assigned' : 'Unassigned'}</Badge>}
      />
      {assignment ? (
        <dl className="detail-list">
          <div><dt>Prompt</dt><dd>{assignment.promptVersion}</dd></div>
          <div><dt>Agent</dt><dd>{assignment.agentId}</dd></div>
          <div><dt>Since</dt><dd>{formatDate(assignment.assignedAt)}</dd></div>
        </dl>
      ) : <EmptyState message={`No ${title.toLowerCase()} assignment.`} />}
    </Card>
  )
}

function MetricCards({ snapshot }: { snapshot: MetricSnapshot }) {
  return (
    <div className="metric-grid">
      {metricNames.map((name) => (
        <Card key={name} className="metric-card">
          <Text size={200}>{metricLabels[name]}</Text>
          <Text size={700} weight="semibold">{formatPercent(snapshot.metrics[name])}</Text>
        </Card>
      ))}
      <Card className="metric-card">
        <Text size={200}>Failure rate</Text>
        <Text size={700} weight="semibold">
          {formatPercent(snapshot.sampleCount ? snapshot.failureCount / snapshot.sampleCount : 0)}
        </Text>
      </Card>
    </div>
  )
}

function QualityChart({ snapshots }: { snapshots: MetricSnapshot[] }) {
  const data = [...snapshots]
    .sort((a, b) => Date.parse(a.windowEnd) - Date.parse(b.windowEnd))
    .slice(-12)
    .map((snapshot) => ({
      date: new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(new Date(snapshot.windowEnd)),
      ...snapshot.metrics,
    }))

  return (
    <div className="chart-container" role="img" aria-label="Line chart of task adherence, groundedness, and tool accuracy">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={data} margin={{ top: 12, right: 16, bottom: 8, left: 0 }}>
          <CartesianGrid stroke="var(--cp-border)" strokeDasharray="4 4" />
          <XAxis dataKey="date" tick={{ fill: 'var(--cp-text-muted)' }} />
          <YAxis domain={[0, 1]} tickFormatter={(value: number) => `${Math.round(value * 100)}%`} tick={{ fill: 'var(--cp-text-muted)' }} />
          <Tooltip formatter={(value) => formatPercent(Number(value))} contentStyle={{ background: 'var(--cp-surface)', borderColor: 'var(--cp-border)' }} />
          <Legend />
          <Line name="Task adherence" type="monotone" dataKey="taskAdherence" stroke="var(--cp-accent)" strokeWidth={2} />
          <Line name="Groundedness" type="monotone" dataKey="groundedness" stroke="var(--cp-success)" strokeWidth={2} />
          <Line name="Tool accuracy" type="monotone" dataKey="toolAccuracy" stroke="var(--cp-link)" strokeWidth={2} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}

function DashboardSkeleton() {
  return (
    <section aria-busy="true" aria-label="Loading operations overview">
      <Skeleton className="dashboard-skeleton">
        <SkeletonItem size={32} />
        <SkeletonItem size={16} />
        <div className="assignment-grid">
          <SkeletonItem className="skeleton-card" />
          <SkeletonItem className="skeleton-card" />
        </div>
        <SkeletonItem className="skeleton-chart" />
      </Skeleton>
    </section>
  )
}

function EmptyState({ message }: { message: string }) {
  return <Text className="empty-state">{message}</Text>
}

function ReleaseFromAssignment({ assignment }: { assignment: AgentAssignment }) {
  return (
    <div className="release-summary">
      <Badge color="success">Current production</Badge>
      <Text weight="semibold">{assignment.promptVersion}</Text>
      <Text>{formatDate(assignment.assignedAt)}</Text>
    </div>
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

function formatPercent(value: number | undefined) {
  return value === undefined ? '—' : new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 }).format(value)
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}
