/// <reference lib="webworker" />
import { clientsClaim } from 'workbox-core'
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching'
import { registerRoute } from 'workbox-routing'
import { NetworkOnly } from 'workbox-strategies'

declare const self: ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<never> }

clientsClaim()
cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)

const excludedNavigation = /^\/(?:api|help|health|swagger)(?:\/|$)/
registerRoute(
  ({ request, url }) => request.mode === 'navigate' && !excludedNavigation.test(url.pathname),
  new NetworkOnly({
    plugins: [{
      handlerDidError: async () => (await caches.match('/offline.html')) ?? Response.error()
    }]
  })
)

self.addEventListener('message', (event) => {
  if (event.data?.type === 'SKIP_WAITING') void self.skipWaiting()
})
