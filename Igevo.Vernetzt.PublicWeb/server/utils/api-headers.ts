import type { H3Event } from 'h3'

/** Header carrying the shared key the API requires for every public API request. */
export const API_KEY_HEADER = 'x-vernetzt-api-key'

/**
 * Headers every server-side request to the Vernetzt API needs. The key lives only in the
 * private runtime config (NUXT_API_ACCESS_KEY) and never reaches the browser.
 */
export function apiRequestHeaders(event: H3Event): Record<string, string> {
  const headers: Record<string, string> = { 'user-agent': 'Vernetzt-Public/1.0' }
  const key = useRuntimeConfig(event).apiAccessKey
  if (key) headers[API_KEY_HEADER] = key
  return headers
}
