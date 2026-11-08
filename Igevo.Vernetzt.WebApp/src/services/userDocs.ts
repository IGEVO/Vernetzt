/** Public user documentation (GitHub Pages), shared by all Vernetzt instances. */
export const USER_DOCS_URL = 'https://igevo.github.io/Vernetzt/'

/** Builds a link to a page of the user documentation, e.g. `userDocsUrl('app/amtsadressen/')`. */
export function userDocsUrl(page = ''): string {
  return USER_DOCS_URL + page.replace(/^\/+/, '')
}
