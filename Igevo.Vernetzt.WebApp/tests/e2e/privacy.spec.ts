import { expect, expectAccessible, test } from './support/fixtures'

test.describe('Datenschutzerklärung', () => {
  test('eigener Text ist ohne Anmeldung lesbar', async ({ page, api }) => {
    api.get('/api/settings/privacy-policy', { mode: 'text', html: '<h2>Verantwortliche Stelle</h2><p>Musterverein e. V.</p><img src=x onerror=alert(1)>' })
    await page.goto('/datenschutz')

    await expect(page.getByRole('heading', { name: 'Datenschutzerklärung', level: 1 })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Verantwortliche Stelle' })).toBeVisible()
    await expect(page.getByText('Musterverein e. V.')).toBeVisible()
    await expect(page.locator('.privacy-text img')).toHaveCount(0)
    await expectAccessible(page, '.v-card')
  })

  test('ohne hinterlegte Erklärung erscheint ein Hinweis', async ({ page, api }) => {
    api.get('/api/settings/privacy-policy', { mode: 'text', html: null })
    await page.goto('/datenschutz')

    await expect(page.getByText('Es ist noch keine Datenschutzerklärung hinterlegt.')).toBeVisible()
  })

  test('Anmeldeseite verlinkt die Datenschutzerklärung', async ({ page }) => {
    await page.goto('/login')

    await expect(page.getByRole('link', { name: 'Datenschutzerklärung' })).toHaveAttribute('href', '/datenschutz')
  })
})
