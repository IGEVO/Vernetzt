import type { Page, Request, Route } from '@playwright/test'

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'DELETE'

export interface MockRequest {
  method: HttpMethod
  path: string
  params: Record<string, string>
  query: URLSearchParams
  body: unknown
}

/** Explicit HTTP response, e.g. `raw(404, { success: false, error: { code: 'NOT_FOUND', message: '…' } })`. */
export class RawResponse {
  constructor(readonly status: number, readonly json?: unknown) {}
}
export const raw = (status: number, json?: unknown) => new RawResponse(status, json)
/** Server-side failure in the API envelope (`success: false`). */
export const fail = (status: number, message: string, code = 'ERROR') => raw(status, { success: false, data: null, error: { code, message } })

type Handler = (request: MockRequest) => unknown | Promise<unknown>
/** A fixed response value (wrapped in the API envelope) – anything except a function. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type StaticValue = string | number | boolean | null | undefined | readonly unknown[] | RawResponse | { [key: string]: any }
interface RouteEntry { method: HttpMethod | '*'; pattern: RegExp; keys: string[]; handler: Handler }
export interface RecordedCall { method: HttpMethod; path: string; query: URLSearchParams; body: unknown }

function compile(template: string): { pattern: RegExp; keys: string[] } {
  const keys: string[] = []
  const source = template
    .replace(/[.+?^${}()|[\]\\]/g, '\\$&')
    .replace(/:(\w+)/g, (_, key: string) => { keys.push(key); return '([^/]+)' })
  return { pattern: new RegExp(`^${source}$`), keys }
}

/**
 * In-memory replacement for the Vernetzt API, installed via `page.route('/api/**')`.
 *
 * - `on(method, '/api/clubs/:id', handler | data)` registers a route; later registrations win, so tests can
 *   override the defaults from `fixtures.ts`.
 * - Plain return values are wrapped in the API envelope `{ success: true, data }`; use `raw()` / `fail()` for errors.
 * - Every request is recorded in `calls` so tests can assert what the UI sent.
 * - Unmocked endpoints answer 404 and are collected in `unhandled`; the fixture fails the test on them,
 *   so a forgotten mock never silently turns into an empty page.
 */
export class MockApi {
  private readonly routes: RouteEntry[] = []
  readonly calls: RecordedCall[] = []
  readonly unhandled: string[] = []
  /** Endpoints that may be called without a mock (fire-and-forget calls irrelevant to the tests). */
  readonly ignored: RegExp[] = [/^GET \/api\/version$/]

  on(method: HttpMethod | '*', template: string, handler: Handler | StaticValue): this {
    const { pattern, keys } = compile(template)
    this.routes.unshift({ method, pattern, keys, handler: typeof handler === 'function' ? handler as Handler : () => handler })
    return this
  }
  get(template: string, handler: Handler | StaticValue): this { return this.on('GET', template, handler) }
  post(template: string, handler: Handler | StaticValue): this { return this.on('POST', template, handler) }
  put(template: string, handler: Handler | StaticValue): this { return this.on('PUT', template, handler) }
  delete(template: string, handler: Handler | StaticValue): this { return this.on('DELETE', template, handler) }

  /** Recorded calls for a method and path (string = exact path, RegExp = pattern). */
  callsTo(method: HttpMethod, path: string | RegExp): RecordedCall[] {
    return this.calls.filter((call) => call.method === method && (typeof path === 'string' ? call.path === path : path.test(call.path)))
  }
  lastBody<T = Record<string, unknown>>(method: HttpMethod, path: string | RegExp): T {
    const calls = this.callsTo(method, path)
    if (!calls.length) throw new Error(`No ${method} call to ${String(path)}. Calls: ${this.calls.map((c) => `${c.method} ${c.path}`).join(', ')}`)
    return calls[calls.length - 1]!.body as T
  }

  async install(page: Page): Promise<void> {
    await page.route((url) => url.pathname.startsWith('/api/'), (route) => this.handle(route, route.request()))
  }

  private async handle(route: Route, request: Request): Promise<void> {
    const url = new URL(request.url())
    const method = request.method() as HttpMethod
    let body: unknown = undefined
    const text = request.postData()
    if (text) { try { body = JSON.parse(text) } catch { body = text } }
    this.calls.push({ method, path: url.pathname, query: url.searchParams, body })

    for (const entry of this.routes) {
      if (entry.method !== '*' && entry.method !== method) continue
      const match = entry.pattern.exec(url.pathname)
      if (!match) continue
      const params = Object.fromEntries(entry.keys.map((key, index) => [key, decodeURIComponent(match[index + 1] ?? '')]))
      const result = await entry.handler({ method, path: url.pathname, params, query: url.searchParams, body })
      if (result instanceof RawResponse) {
        await route.fulfill(result.json === undefined ? { status: result.status } : { status: result.status, json: result.json })
      } else {
        await route.fulfill({ json: { success: true, data: result ?? null } })
      }
      return
    }

    const key = `${method} ${url.pathname}`
    if (!this.ignored.some((pattern) => pattern.test(key))) this.unhandled.push(key)
    await route.fulfill({ status: 404, json: { success: false, data: null, error: { code: 'NOT_MOCKED', message: `Not mocked: ${key}` } } })
  }
}
