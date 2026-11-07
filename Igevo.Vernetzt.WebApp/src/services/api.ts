import type { ApiResponse } from '@/types/api'

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly errors: string[] = [],
    readonly code?: string
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

let authFailureHandler: (() => void) | null = null
let passkeyRequiredHandler: (() => void) | null = null

/** Server error code: the user's role needs a passkey session, but the session was started with a login link. */
export const PASSKEY_REQUIRED = 'PASSKEY_REQUIRED'

export function onAuthenticationFailure(handler: () => void): void {
  authFailureHandler = handler
}

export function onPasskeyRequired(handler: () => void): void {
  passkeyRequiredHandler = handler
}

async function parseResponse<T>(response: Response): Promise<T> {
  if (response.status === 204) return undefined as T
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('application/json')) return await response.text() as T
  const envelope = await response.json() as ApiResponse<T> | T
  if (typeof envelope === 'object' && envelope !== null && 'success' in envelope) {
    const result = envelope as ApiResponse<T>
    if (!result.success) throw new ApiError(result.error?.message ?? result.message ?? 'Die Anfrage ist fehlgeschlagen.', response.status, result.errors ?? result.error?.details ?? [], result.error?.code)
    return result.data as T
  }
  return envelope as T
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')

  const response = await fetch(path, { ...init, headers, credentials: 'same-origin' })
  // The session is a sliding HttpOnly cookie managed by the server; a 401 means it has ended.
  const isAuthEndpoint = path.startsWith('/api/auth/')
  if (!response.ok) {
    if (response.status === 401 && !isAuthEndpoint) authFailureHandler?.()
    let message = `Fehler ${response.status}`
    let errors: string[] = []
    let code: string | undefined
    try {
      const body = await response.json() as ApiResponse<unknown>
      message = body.error?.message ?? body.message ?? message
      errors = body.errors ?? body.error?.details ?? []
      code = body.error?.code
    } catch { /* Response has no JSON body. */ }
    if (response.status === 403 && code === PASSKEY_REQUIRED) passkeyRequiredHandler?.()
    throw new ApiError(message, response.status, errors, code)
  }
  return parseResponse<T>(response)
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => apiRequest<T>(path, { signal }),
  post: <T>(path: string, body?: unknown, signal?: AbortSignal) => apiRequest<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body), signal }),
  postForm: <T>(path: string, body: FormData, signal?: AbortSignal) => apiRequest<T>(path, { method: 'POST', body, signal }),
  put: <T>(path: string, body?: unknown, signal?: AbortSignal) => apiRequest<T>(path, { method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body), signal }),
  delete: <T = void>(path: string, signal?: AbortSignal) => apiRequest<T>(path, { method: 'DELETE', signal })
}

export function toQuery(params: Record<string, string | number | boolean | null | undefined>): string {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) if (value !== null && value !== undefined && value !== '') query.set(key, String(value))
  const value = query.toString()
  return value ? `?${value}` : ''
}
