/* oxlint-disable react/only-export-components */
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { queryKeys } from '../api/queries'
import type {
  EvaluationUpdated,
  ExperimentUpdated,
  MetricsUpdated,
  ReleaseUpdated,
} from '../api/types'

export type RealtimeStatus = 'connecting' | 'connected' | 'offline'
export type RealtimeConnectionFactory = () => HubConnection

interface RealtimeState {
  status: RealtimeStatus
  activeExperiment: ExperimentUpdated | null
  recentReleases: ReleaseUpdated[]
}

const RealtimeContext = createContext<RealtimeState>({
  status: 'connecting',
  activeExperiment: null,
  recentReleases: [],
})

const defaultFactory: RealtimeConnectionFactory = () => {
  const apiBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ?? ''
  return new HubConnectionBuilder()
    .withUrl(`${apiBase}/hubs/realtime`)
    .withAutomaticReconnect([0, 2_000, 10_000, 30_000])
    .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
    .build()
}

export function RealtimeProvider({
  children,
  factory = defaultFactory,
}: {
  children: ReactNode
  factory?: RealtimeConnectionFactory
}) {
  const client = useQueryClient()
  const [status, setStatus] = useState<RealtimeStatus>('connecting')
  const [activeExperiment, setActiveExperiment] = useState<ExperimentUpdated | null>(null)
  const [recentReleases, setRecentReleases] = useState<ReleaseUpdated[]>([])

  useEffect(() => {
    const connection = factory()
    let disposed = false

    connection.on('MetricsUpdated', (_update: MetricsUpdated) => {
      void client.invalidateQueries({ queryKey: queryKeys.metrics })
    })
    connection.on('EvaluationUpdated', (_update: EvaluationUpdated) => {
      void client.invalidateQueries({ queryKey: queryKeys.evaluations })
      void client.invalidateQueries({ queryKey: queryKeys.metrics })
    })
    connection.on('ExperimentUpdated', (update: ExperimentUpdated) => {
      setActiveExperiment(update.lifecycle.toLowerCase() === 'ended' ? null : update)
    })
    connection.on('ReleaseUpdated', (update: ReleaseUpdated) => {
      setRecentReleases((current) => [update, ...current].slice(0, 5))
      void client.invalidateQueries({ queryKey: queryKeys.versions })
      void client.invalidateQueries({ queryKey: queryKeys.metrics })
      void client.invalidateQueries({ queryKey: queryKeys.releases })
    })
    connection.onreconnecting(() => !disposed && setStatus('connecting'))
    connection.onreconnected(() => !disposed && setStatus('connected'))
    connection.onclose(() => !disposed && setStatus('offline'))

    void connection.start()
      .then(() => {
        if (disposed) {
          void connection.stop()
        } else {
          setStatus('connected')
        }
      })
      .catch(() => {
        if (!disposed) setStatus('offline')
      })

    return () => {
      disposed = true
      if (
        connection.state === HubConnectionState.Connected
        || connection.state === HubConnectionState.Reconnecting
      ) {
        void connection.stop()
      }
    }
  }, [client, factory])

  const value = useMemo(
    () => ({ status, activeExperiment, recentReleases }),
    [status, activeExperiment, recentReleases],
  )
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>
}

export const useRealtime = () => useContext(RealtimeContext)
