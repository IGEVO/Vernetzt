import { QueryClient } from '@tanstack/vue-query'

/** Shared query client, so the auth store can drop all cached data when the signed-in user changes. */
export const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false }, mutations: { retry: false } }
})
