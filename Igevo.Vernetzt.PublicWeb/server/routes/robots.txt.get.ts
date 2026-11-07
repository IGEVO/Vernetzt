export default defineEventHandler((event) => {
  setHeader(event, 'content-type', 'text/plain; charset=utf-8')
  const siteUrl = useRuntimeConfig(event).public.siteUrl.replace(/\/$/, '')
  return `User-agent: *\nAllow: /\nSitemap: ${siteUrl}/sitemap.xml\n`
})
