import type {
  MetricsResponse,
  ProblemDetails,
  ReleasesResponse,
  ReleaseMutationResponse,
  VersionsResponse,
} from './types'

export class ApiError extends Error {
  readonly status: number
  readonly problem?: ProblemDetails

  constructor(message: string, status: number, problem?: ProblemDetails) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

const baseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ?? ''

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...init?.headers,
    },
  })

  if (!response.ok) {
    let detail = `Request failed with status ${response.status}.`
    let problem: ProblemDetails | undefined
    try {
      problem = await response.json() as ProblemDetails
      detail = problem.detail ?? problem.title ?? detail
    } catch {
      // The status remains actionable when a non-JSON proxy response is returned.
    }
    throw new ApiError(detail, response.status, problem)
  }

  return response.json() as Promise<T>
}

export const api = {
  versions: (signal?: AbortSignal) =>
    request<VersionsResponse>('/api/versions?pageSize=50', { signal }),
  metrics: (signal?: AbortSignal) =>
    request<MetricsResponse>('/api/metrics?pageSize=100', { signal }),
  releases: (signal?: AbortSignal) =>
    request<ReleasesResponse>('/api/releases?pageSize=100', { signal }),
  promote: (version: string, eTag: string, actor: string, idempotencyKey: string) =>
    request<ReleaseMutationResponse>(`/api/promote/${encodeURIComponent(version)}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'If-Match': eTag,
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify({ actor }),
    }),
  rollback: (eTag: string, actor: string, idempotencyKey: string) =>
    request<ReleaseMutationResponse>('/api/rollback', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'If-Match': eTag,
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify({ actor }),
    }),
}
