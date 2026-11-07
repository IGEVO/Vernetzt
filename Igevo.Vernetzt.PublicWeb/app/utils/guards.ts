const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function isGuid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value)
}

/** Only http(s) links are rendered as external links; anything else is dropped. */
export function safeExternalUrl(value: string | null | undefined): string | undefined {
  if (!value) return undefined
  try {
    const url = new URL(value)
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.toString() : undefined
  } catch {
    return undefined
  }
}

/** Host name without "www." for link labels, e.g. "tsv-beispiel.de". */
export function displayHost(value: string): string {
  try { return new URL(value).hostname.replace(/^www\./, '') } catch { return value }
}
