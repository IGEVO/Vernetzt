interface Page<T> { items?: T[]; totalPages?: number }
interface ClubSummary { id?: string; name?: string }
interface DepartmentSummary { id?: string; name?: string }
interface EventSummary { id?: string; title?: string }

const slug = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '')
  .replace(/ß/g, 'ss').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'eintrag'

const escapeXml = (value: string) => value.replace(/[<>&"']/g, character => ({
  '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;'
})[character]!)

// Building the sitemap pages through the whole API, so the result is cached instead of being
// rebuilt on every request (which would make the sitemap an easy load amplifier).
export default defineCachedEventHandler(async (event) => {
  const config = useRuntimeConfig(event)
  const api = config.apiBaseUrl.replace(/\/$/, '')
  const site = config.public.siteUrl.replace(/\/$/, '')
  const headers = apiRequestHeaders(event)
  const paths = new Set(['/', '/vereine', '/veranstaltungen', '/datenschutz', '/impressum'])

  for (let page = 1, pages = 1; page <= pages; page++) {
    const result = await $fetch<Page<ClubSummary>>(`${api}/api/public/v1/clubs`, { query: { page, pageSize: 100 }, headers })
    pages = result.totalPages ?? 1
    for (const club of result.items ?? []) {
      if (!club.id || !club.name) continue
      paths.add(`/vereine/${club.id}/${slug(club.name)}`)
    }
  }

  for (let page = 1, pages = 1; page <= pages; page++) {
    const result = await $fetch<Page<DepartmentSummary>>(`${api}/api/public/v1/departments`, { query: { page, pageSize: 100 }, headers })
    pages = result.totalPages ?? 1
    for (const department of result.items ?? []) {
      if (department.id && department.name) paths.add(`/abteilungen/${department.id}/${slug(department.name)}`)
    }
  }

  for (let page = 1, pages = 1; page <= pages; page++) {
    const result = await $fetch<Page<EventSummary>>(`${api}/api/public/v1/events`, { query: { page, pageSize: 100 }, headers })
    pages = result.totalPages ?? 1
    for (const item of result.items ?? []) {
      if (item.id && item.title) paths.add(`/veranstaltungen/${item.id}/${slug(item.title)}`)
    }
  }

  setHeader(event, 'content-type', 'application/xml; charset=utf-8')
  const urls = [...paths].map(path => `  <url><loc>${escapeXml(`${site}${path}`)}</loc></url>`).join('\n')
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls}\n</urlset>\n`
}, { name: 'sitemap', maxAge: 60 * 60, swr: true, getKey: () => 'sitemap.xml' })
