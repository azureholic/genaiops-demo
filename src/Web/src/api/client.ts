import type { MetricsResponse, VersionsResponse } from './types'

export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

const baseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ?? ''

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    headers: { Accept: 'application/json' },
    signal,
  })

  if (!response.ok) {
    let detail = `Request failed with status ${response.status}.`
    try {
      const problem = await response.json() as { detail?: string; title?: string }
      detail = problem.detail ?? problem.title ?? detail
    } catch {
      // The status remains actionable when a non-JSON proxy response is returned.
    }
    throw new ApiError(detail, response.status)
  }

  return response.json() as Promise<T>
}

export const api = {
  versions: (signal?: AbortSignal) =>
    get<VersionsResponse>('/api/versions?pageSize=50', signal),
  metrics: (signal?: AbortSignal) =>
    get<MetricsResponse>('/api/metrics?pageSize=100', signal),
}
