import type { PublicPortal } from '~/types/public'

/**
 * Portal texts and branding maintained by system admins. Loaded once during server rendering and
 * reused from the payload on client navigation. Falls back to null (default texts) on errors.
 */
export function usePortal() {
  return useFetch<PublicPortal | null>('/api/portal', {
    key: 'portal',
    default: () => null,
    getCachedData: (key, nuxtApp) => nuxtApp.payload.data[key] ?? nuxtApp.static.data[key]
  })
}
