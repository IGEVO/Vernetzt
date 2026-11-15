/// <reference lib="webworker" />
import { clientsClaim } from 'workbox-core'
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching'
import { registerRoute } from 'workbox-routing'
import { NetworkOnly } from 'workbox-strategies'

declare const self: ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<never> }

clientsClaim()
cleanupOutdatedCaches()
const manifest = self.__WB_MANIFEST
precacheAndRoute(manifest)

const excludedNavigation = /^\/(?:api|help|health|swagger)(?:\/|$)/
registerRoute(
  ({ request, url }) => request.mode === 'navigate' && !excludedNavigation.test(url.pathname),
  new NetworkOnly({
    plugins: [{
      handlerDidError: async () => (await caches.match('/offline.html')) ?? Response.error()
    }]
  })
)

const precachedUrls = new Set(
  (manifest as Array<string | { url: string }>).map((entry) =>
    new URL(typeof entry === 'string' ? entry : entry.url, self.location.href).href)
)

self.addEventListener('message', (event) => {
  if (event.data?.type === 'SKIP_WAITING') void self.skipWaiting()
  // Lets the page find out whether it already runs the build of this (waiting) worker.
  if (event.data?.type === 'HAS_ASSET') event.ports[0]?.postMessage(precachedUrls.has(event.data.url))
})
