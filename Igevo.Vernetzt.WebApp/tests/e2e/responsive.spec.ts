import { data, expect, test } from './support/fixtures'

/** Runs in all browser projects (desktop and mobile): layout must never scroll horizontally. */
const pages = [
  { name: 'Dashboard', path: '/' },
  { name: 'Vereine', path: '/clubs' },
  { name: 'Vereinsdetails', path: `/clubs/${data.ids.club}` },
  { name: 'Kontakte', path: '/kontakte' },
  { name: 'Mein Konto', path: '/konto' }
]

test.describe('Responsives Layout', () => {
  test.beforeEach(async ({ api, signIn }) => {
    await signIn()
    api
      .get('/api/clubs/search', data.paged([data.clubSummary(), data.clubSummary({ id: data.ids.club2, name: 'Förderverein der Grundschule am Musterbach e. V.' })]))
      .get(`/api/clubs/${data.ids.club}`, data.club())
      .get('/api/users/contacts/search', data.paged([data.contact()]))
  })

  for (const { name, path } of pages) {
    test(`${name} ohne horizontales Scrollen`, async ({ page }) => {
      await page.goto(path)
      await expect(page.locator('h1')).toBeVisible()
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
      expect(overflow).toBeLessThanOrEqual(0)
    })
  }

  test('Navigation ist erreichbar', async ({ page, isMobile }) => {
    await page.goto('/')
    if (isMobile) await page.getByRole('button', { name: 'Navigation umschalten' }).click()
    await page.getByRole('navigation').getByRole('link', { name: 'Kontakte' }).click()
    await expect(page.getByRole('heading', { name: 'Kontakte', level: 1 })).toBeVisible()
  })
})
