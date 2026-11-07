import { isIP } from 'node:net'
import type { H3Event } from 'h3'

/**
 * Internal header that carries the resolved client IP from the SSR request into the
 * nested local fetches (useFetch on the server). It is overwritten for every external
 * request, so a client can never inject its own value.
 */
export const CLIENT_IP_HEADER = 'x-vernetzt-client-ip'

/** Nitro marks requests created by its in-process local fetch with this property. */
export function isInternalRequest(event: H3Event): boolean {
  return Boolean((event.node.req as { __unenv__?: unknown }).__unenv__)
}

/**
 * Determines the real client IP. Without trusted proxies only the socket address counts.
 * With N trusted reverse proxies in front of the portal, the N-th entry from the right of
 * X-Forwarded-For is the address the outermost trusted proxy saw; everything left of it
 * is client controlled and ignored.
 */
export function resolveClientIp(event: H3Event): string | undefined {
  const hops = Math.max(0, Math.trunc(Number(useRuntimeConfig(event).trustedProxyHops) || 0))
  let ip: string | undefined

  if (hops > 0) {
    const chain = String(event.node.req.headers['x-forwarded-for'] ?? '')
      .split(',').map(part => part.trim()).filter(Boolean)
    ip = chain.length >= hops ? chain[chain.length - hops] : undefined
  }

  ip ??= event.node.req.socket?.remoteAddress
  if (ip?.startsWith('::ffff:')) ip = ip.slice(7)
  return ip && isIP(ip) ? ip : undefined
}
