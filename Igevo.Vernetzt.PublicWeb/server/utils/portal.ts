import type { H3Event } from 'h3'
import type { PublicPortal } from '../../app/types/public'

/**
 * Portal texts and branding, needed by every page render. Cached per portal instance so traffic
 * never multiplies API calls: at most one upstream request per minute, and while revalidating
 * (or if the API is briefly unreachable) the last known state is served.
 */
export const fetchPortal = defineCachedFunction(async (event: H3Event): Promise<PublicPortal> => {
  const api = useRuntimeConfig(event).apiBaseUrl.replace(/\/$/, '')
  return await $fetch<PublicPortal>(`${api}/api/public/v1/portal`, {
    headers: apiRequestHeaders(event)
  })
}, { name: 'portal', maxAge: 60, swr: true, getKey: () => 'current' })
