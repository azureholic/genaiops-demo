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
    versions: [],
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
  })
})
