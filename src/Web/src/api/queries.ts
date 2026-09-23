import { queryOptions } from '@tanstack/react-query'
import { api } from './client'

export const queryKeys = {
  versions: ['versions'] as const,
  metrics: ['metrics'] as const,
  evaluations: ['evaluations'] as const,
  releases: ['releases'] as const,
}

export const versionsQuery = queryOptions({
  queryKey: queryKeys.versions,
  queryFn: ({ signal }) => api.versions(signal),
})

export const metricsQuery = queryOptions({
  queryKey: queryKeys.metrics,
  queryFn: ({ signal }) => api.metrics(signal),
})

export const releasesQuery = queryOptions({
  queryKey: queryKeys.releases,
  queryFn: ({ signal }) => api.releases(signal),
})
