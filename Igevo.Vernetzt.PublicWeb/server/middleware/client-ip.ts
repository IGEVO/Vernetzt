// Resolve the client IP once per external request and pin it in an internal header, so that
// server-side API calls made while rendering can forward it to the API rate limiter.
export default defineEventHandler((event) => {
  if (isInternalRequest(event)) return

  const ip = resolveClientIp(event)
  if (ip) event.node.req.headers[CLIENT_IP_HEADER] = ip
  else delete event.node.req.headers[CLIENT_IP_HEADER]
})
