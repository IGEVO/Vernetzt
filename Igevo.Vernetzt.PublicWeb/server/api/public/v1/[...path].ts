import { createError, getRequestHeader, sendProxy, setResponseHeader } from 'h3'

// Headers that are useful for the read-only public API. Everything else (cookies,
// authorization, client supplied forwarding headers) is deliberately not forwarded.
const FORWARDED_REQUEST_HEADERS = ['accept', 'accept-language', 'if-none-match', 'if-modified-since'] as const

export default defineEventHandler(async (event) => {
  const method = event.method
  if (method !== 'GET' && method !== 'HEAD') {
    setResponseHeader(event, 'allow', 'GET, HEAD')
    throw createError({ statusCode: 405, statusMessage: 'Method Not Allowed' })
  }

  const upstreamPath = toAllowedPublicApiPath(event.path)
  if (!upstreamPath) throw createError({ statusCode: 404, statusMessage: 'Not Found' })

  const config = useRuntimeConfig(event)
  const headers = apiRequestHeaders(event)
  for (const name of FORWARDED_REQUEST_HEADERS) {
    const value = getRequestHeader(event, name)
    if (value) headers[name] = value
  }

  // The API partitions its rate limit by client IP. It must list the portal as known proxy
  // (ReverseProxy:KnownProxies) to use this value instead of the portal's own address.
  const clientIp = getRequestHeader(event, CLIENT_IP_HEADER)
  if (clientIp) headers['x-forwarded-for'] = clientIp

  return sendProxy(event, `${config.apiBaseUrl.replace(/\/$/, '')}${upstreamPath}`, {
    fetchOptions: { method, headers, redirect: 'manual' }
  })
})
