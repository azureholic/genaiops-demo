// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { QueryClient } from '@tanstack/react-query'
import { act, cleanup, configure, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

Object.defineProperty(globalThis, 'NodeFilter', {
  configurable: true,
  value: window.NodeFilter,
})
configure({ asyncUtilTimeout: 5_000 })

const assignment = {
  agentId: 'support-agent',
  promptVersion: 'v2',
  slot: 'Production',
  assignedAt: '2026-09-23T10:00:00Z',
}

const responses = {
  '/api/versions?pageSize=50': {
    registryId: 'default',
    eTag: '"1"',
    production: assignment,
    candidate: { ...assignment, agentId: 'candidate-agent', promptVersion: 'v3', slot: 'Candidate' },
    versions: [
      {
        id: 'v1',
        version: 'v1',
        displayName: 'Standard support prompt',
        lifecycle: 'Retired',
        createdAt: '2026-09-01T10:00:00Z',
        expectedMetrics: { taskAdherence: 0.82, groundedness: 0.89, toolAccuracy: 0.84 },
      },
      {
        id: 'v2',
        version: 'v2',
        displayName: 'Improved support prompt',
        lifecycle: 'Production',
        createdAt: '2026-09-10T10:00:00Z',
        expectedMetrics: { taskAdherence: 0.94, groundedness: 0.97, toolAccuracy: 0.95 },
      },
      {
        id: 'v3',
        version: 'v3',
        displayName: 'Intentionally poor support prompt',
        lifecycle: 'Candidate',
        createdAt: '2026-09-20T10:00:00Z',
        expectedMetrics: { taskAdherence: 0.72, groundedness: 0.75, toolAccuracy: 0.58 },
      },
    ],
    continuationToken: null,
  },
  '/api/metrics?pageSize=100': {
    registryId: 'default',
    snapshots: [{
      id: 'metric-1',
      promptVersion: 'v2',
      windowStart: '2026-09-22T10:00:00Z',
      windowEnd: '2026-09-23T10:00:00Z',
      generatedAt: '2026-09-23T10:01:00Z',
      sampleCount: 20,
      successfulCount: 19,
      failureCount: 1,
      metrics: { taskAdherence: 0.94, groundedness: 0.97, toolAccuracy: 0.95 },
    }],
    continuationToken: null,
  },
  '/api/releases?pageSize=100': {
    registryId: 'default',
    releases: [{
      id: 'release-v3-rejected',
      registryId: 'default',
      operation: 'Promotion',
      promptVersion: 'v3',
      agentId: 'candidate-agent',
      lifecycle: 'Rejected',
      actor: 'release-manager',
      createdAt: '2026-09-23T11:00:00Z',
      previousProduction: assignment,
      newProduction: null,
      rollbackTarget: assignment,
      gateEvidence: {
        metricSnapshotId: 'metric-v3',
        windowStart: '2026-09-22T10:00:00Z',
        windowEnd: '2026-09-23T10:00:00Z',
        sampleCount: 20,
        observed: { taskAdherence: 0.72, groundedness: 0.75, toolAccuracy: 0.58 },
        thresholds: { taskAdherence: 0.9, groundedness: 0.9, toolAccuracy: 0.9 },
        passed: false,
        reasons: ['toolAccuracy 0.58 is below 0.9'],
      },
    }, {
      id: 'release-v2',
      registryId: 'default',
      operation: 'Promotion',
      promptVersion: 'v2',
      agentId: 'support-agent',
      lifecycle: 'Promoted',
      actor: 'release-manager',
      createdAt: '2026-09-23T10:00:00Z',
      previousProduction: { ...assignment, agentId: 'legacy-agent', promptVersion: 'v1' },
      newProduction: assignment,
      rollbackTarget: null,
      gateEvidence: {
        metricSnapshotId: 'metric-1',
        windowStart: '2026-09-22T10:00:00Z',
        windowEnd: '2026-09-23T10:00:00Z',
        sampleCount: 20,
        observed: { taskAdherence: 0.94, groundedness: 0.97, toolAccuracy: 0.95 },
        thresholds: { taskAdherence: 0.9, groundedness: 0.9, toolAccuracy: 0.9 },
        passed: true,
        reasons: [],
      },
    }],
    continuationToken: null,
  },
}

function connectionFactory() {
  const handlers = new Map<string, (...args: unknown[]) => void>()
  const connection = {
    state: HubConnectionState.Disconnected,
    on: vi.fn((event: string, handler: (...args: unknown[]) => void) => handlers.set(event, handler)),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
  } as unknown as HubConnection
  return { connection, handlers }
}

function renderApp(factory = connectionFactory()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  const result = render(<App client={client} realtimeFactory={() => factory.connection} />)
  return { ...result, ...factory, client }
}

beforeEach(() => {
  vi.stubGlobal('ResizeObserver', class {
    observe() {}
    unobserve() {}
    disconnect() {}
  })
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
    const path = new URL(String(input), 'http://localhost').pathname + new URL(String(input), 'http://localhost').search
    return Promise.resolve(new Response(JSON.stringify(responses[path as keyof typeof responses]), {
      status: responses[path as keyof typeof responses] ? 200 : 404,
      headers: { 'Content-Type': 'application/json' },
    }))
  }))
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  window.history.replaceState({}, '', '/')
})

describe('application shell', () => {
  it('renders accessible navigation and dashboard data', async () => {
    renderApp()

    expect(await screen.findByRole('heading', { name: 'Operations overview' })).toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Primary navigation' })
    expect(within(nav).getAllByRole('link')).toHaveLength(5)
    expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByText('94%')).toBeInTheDocument()
    expect(screen.getByText('candidate-agent')).toBeInTheDocument()
    expect(screen.getByText('No active experiment reported in this session.')).toBeInTheDocument()
  })

  it('supports keyboard navigation between dashboard routes', async () => {
    const user = userEvent.setup()
    renderApp()
    await screen.findByRole('heading', { name: 'Operations overview' })

    const promptRegistry = screen.getByRole('link', { name: 'Prompt Registry' })
    promptRegistry.focus()
    await user.keyboard('{Enter}')

    expect(await screen.findByRole('heading', { name: 'Prompt Registry' })).toBeInTheDocument()
    expect(promptRegistry).toHaveAttribute('aria-current', 'page')
  })

  it('opens and closes the responsive navigation for touch users', async () => {
    const user = userEvent.setup()
    window.innerWidth = 390
    window.dispatchEvent(new Event('resize'))
    renderApp()
    await screen.findByRole('heading', { name: 'Operations overview' })

    const toggle = screen.getByRole('button', { name: 'Open navigation' })
    await user.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')

    await user.click(screen.getByRole('link', { name: 'Release History' }))
    expect(await screen.findByRole('heading', { name: 'Release History' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open navigation' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('shows an actionable API error state', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(
      JSON.stringify({ title: 'Service unavailable' }),
      { status: 400, headers: { 'Content-Type': 'application/json' } },
    ))
    renderApp()

    expect(await screen.findByRole('alert')).toHaveTextContent('Dashboard data is unavailable')
    expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled()
  })

  it('invalidates typed queries and displays realtime updates', async () => {
    const rendered = renderApp()
    await screen.findByRole('heading', { name: 'Operations overview' })
    const invalidate = vi.spyOn(rendered.client, 'invalidateQueries')

    act(() => {
      rendered.handlers.get('MetricsUpdated')?.({})
      rendered.handlers.get('ExperimentUpdated')?.({
        lifecycle: 'Running',
        allocations: [{ promptVersion: 'v2', percentage: 80 }, { promptVersion: 'v3', percentage: 20 }],
        updatedAt: '2026-09-23T12:00:00Z',
      })
      rendered.handlers.get('ReleaseUpdated')?.({
        operation: 'Promote',
        promptVersion: 'v3',
        lifecycle: 'Completed',
        occurredAt: '2026-09-23T12:00:00Z',
      })
    })

    expect(await screen.findByText('20% traffic')).toBeInTheDocument()
    expect(screen.getByText('Promote: v3')).toBeInTheDocument()
    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ['metrics'] }))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['versions'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['releases'] })
  })

  it('shows version states, comparisons, and failed gate detail', async () => {
    const user = userEvent.setup()
    window.history.replaceState({}, '', '/prompts')
    const rendered = renderApp()

    expect(await screen.findByRole('heading', { name: 'Prompt Registry' })).toBeInTheDocument()
    act(() => {
      rendered.handlers.get('ExperimentUpdated')?.({
        lifecycle: 'Running',
        allocations: [{ promptVersion: 'v1', percentage: 10 }, { promptVersion: 'v2', percentage: 90 }],
        updatedAt: '2026-09-23T12:00:00Z',
      })
    })
    expect(screen.getByRole('heading', { name: /v1 · Standard support prompt/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /v2 · Improved support prompt/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /v3 · Intentionally poor support prompt/ })).toBeInTheDocument()
    expect(screen.getByText('production')).toBeInTheDocument()
    expect(screen.getByText('candidate')).toBeInTheDocument()
    expect(screen.getByText('experiment')).toBeInTheDocument()
    expect(screen.getByText('Promotion blocked by quality gates')).toBeInTheDocument()
    expect(screen.getByText('toolAccuracy 0.58 is below 0.9')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Promote v3' }))
    expect(screen.getByRole('dialog')).toHaveTextContent('Exact target')
    expect(screen.getByRole('dialog')).toHaveTextContent('v3')
  })

  it('keeps a rejected promotion visibly failed', async () => {
    const user = userEvent.setup()
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      const path = url.pathname + url.search
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(JSON.stringify({
          title: 'Quality gate rejected',
          detail: 'Candidate v3 did not satisfy tool accuracy.',
          code: 'quality_gate_rejected',
          correlationId: 'corr-123',
        }), { status: 422, headers: { 'Content-Type': 'application/json' } }))
      }
      return Promise.resolve(new Response(JSON.stringify(responses[path as keyof typeof responses]), {
        status: responses[path as keyof typeof responses] ? 200 : 404,
        headers: { 'Content-Type': 'application/json' },
      }))
    })
    window.history.replaceState({}, '', '/prompts')
    renderApp()
    await user.click(await screen.findByRole('button', { name: 'Promote v3' }))
    await user.click(screen.getByRole('button', { name: 'Promote to production' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Promote to production failed')
    expect(alert).toHaveTextContent('Candidate v3 did not satisfy tool accuracy.')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.queryByText(/promotion succeeded/i)).not.toBeInTheDocument()
  })

  it('confirms the exact rollback target and handles a stale conflict', async () => {
    const user = userEvent.setup()
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      const path = url.pathname + url.search
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(JSON.stringify({
          title: 'Registry precondition failed',
          detail: 'The supplied ETag is stale.',
          code: 'stale_registry',
        }), { status: 412, headers: { 'Content-Type': 'application/json' } }))
      }
      return Promise.resolve(new Response(JSON.stringify(responses[path as keyof typeof responses]), {
        status: responses[path as keyof typeof responses] ? 200 : 404,
        headers: { 'Content-Type': 'application/json' },
      }))
    })
    window.history.replaceState({}, '', '/releases')
    renderApp()

    await user.click(await screen.findByRole('button', { name: 'Roll back production' }))
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveTextContent('v1 (legacy-agent)')
    await user.click(within(dialog).getByRole('button', { name: 'Roll back production' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Registry changed')
    expect(dialog).toBeInTheDocument()
  })
})
