/**
 * Links from the admin UI to the public portal (Igevo.Vernetzt.PublicWeb).
 * `toSlug` and the path format mirror Igevo.Vernetzt.PublicWeb/app/utils/slug.ts; keep both in sync,
 * otherwise the portal answers with a redirect to its canonical URL.
 */
export type PublicWebKind = 'vereine' | 'abteilungen'

export function toSlug(value: string | null | undefined): string {
  return (value ?? '')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/ß/g, 'ss')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '') || 'eintrag'
}

/** Absolute URL of a club's, department's or group's public page; null if no portal URL is configured. */
export function publicWebLink(baseUrl: string | null | undefined, kind: PublicWebKind, id: string, name: string): string | null {
  const base = baseUrl?.trim().replace(/\/+$/, '')
  return base ? `${base}/${kind}/${id}/${toSlug(name)}` : null
}
