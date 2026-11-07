const ID = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
const PREFIX = '/api/public/v1'

/**
 * Exhaustive list of public API routes the portal may forward. Matching happens on the raw,
 * undecoded path, so encoded dot segments (%2e%2e) or slashes (%2f) never match.
 */
const ALLOWED_PATHS = [
  /^\/(clubs|departments|events|club-types)$/,
  /^\/portal\/logo$/,
  new RegExp(`^/(clubs|departments|events)/${ID}$`),
  new RegExp(`^/(clubs|departments)/${ID}/logo$`),
  new RegExp(`^/(clubs|departments|contacts)/${ID}/contact-details$`),
  new RegExp(`^/events/${ID}/media/${ID}/(file|preview/\\d{1,5})$`)
]

const MAX_QUERY_LENGTH = 1024

/** Returns the upstream path including query for an allowed public API request, otherwise null. */
export function toAllowedPublicApiPath(requestPath: string): string | null {
  const queryIndex = requestPath.indexOf('?')
  const path = queryIndex === -1 ? requestPath : requestPath.slice(0, queryIndex)
  const query = queryIndex === -1 ? '' : requestPath.slice(queryIndex)

  if (!path.startsWith(`${PREFIX}/`) || query.length > MAX_QUERY_LENGTH) return null
  const route = path.slice(PREFIX.length)
  return ALLOWED_PATHS.some(pattern => pattern.test(route)) ? `${path}${query}` : null
}
