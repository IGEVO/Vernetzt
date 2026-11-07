import { data, expect, snackbar, test } from './support/fixtures'

test.describe('Mein Konto', () => {
  test.beforeEach(async ({ api, signIn }) => {
    await signIn(data.user({ phoneNumber: '0151 555' }))
    api.get('/api/users/me/contact-assignments', [data.myAssignment()])
  })

  test('alte Adressen leiten auf die passenden Tabs weiter', async ({ page }) => {
    await page.goto('/profil')
    await expect(page).toHaveURL(/\/konto$/)
    await expect(page.getByRole('tab', { name: 'Profil' })).toHaveAttribute('aria-selected', 'true')

    await page.goto('/einstellungen')
    await expect(page).toHaveURL(/\/konto\?tab=sicherheit$/)
    await expect(page.getByText('Passkeys', { exact: true })).toBeVisible()
  })

  test('Benutzermenü führt zu Mein Konto', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('button', { name: 'Benutzermenü' }).click()
    await page.getByRole('link', { name: 'Mein Konto', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Mein Konto' })).toBeVisible()
  })

  test('Profil bearbeiten', async ({ page, api }) => {
    api.put('/api/users/:id', ({ body }) => body)
    await page.goto('/konto')
    await expect(page.getByText('Sichtbar für: Alle registrierten Benutzer').first()).toBeVisible()

    await page.getByRole('button', { name: 'Bearbeiten' }).click()
    await page.getByLabel('Vorname *').fill('Erika Maria')
    await page.getByLabel('Telefonnummer').fill('')
    await page.getByLabel('Telefonnummer').press('Enter')

    await expect(snackbar(page)).toContainText('Profil wurde aktualisiert.')
    expect(api.lastBody('PUT', `/api/users/${data.ids.user}`)).toMatchObject({ firstName: 'Erika Maria', lastName: 'Muster', phoneNumber: null })
  })

  test('Pflichtfelder im Profil werden geprüft', async ({ page, api }) => {
    await page.goto('/konto')
    await page.getByRole('button', { name: 'Bearbeiten' }).click()
    await page.getByLabel('Nachname *').fill('')
    await page.getByRole('button', { name: 'Speichern' }).click()
    await expect(page.getByText('Pflichtfeld')).toBeVisible()
    expect(api.callsTo('PUT', /\/api\/users\//)).toHaveLength(0)
  })

  test('ungeeignete Bilddatei wird abgelehnt', async ({ page, api }) => {
    await page.goto('/konto')
    await page.locator('input[type=file]').setInputFiles({ name: 'notizen.txt', mimeType: 'text/plain', buffer: Buffer.from('kein Bild') })
    await expect(snackbar(page)).toContainText('Bitte JPG, PNG, WebP oder HEIC bis maximal 5 MB auswählen.')
    expect(api.callsTo('POST', /\/api\/avatars\//)).toHaveLength(0)
  })

  test('Amtsadresse einer Zuordnung speichern', async ({ page, api }) => {
    api.put('/api/users/me/contact-assignments/:id/email', null)
    await page.goto('/konto?tab=zuordnungen')
    await expect(page.getByText('Persönliche Adresse', { exact: true })).toBeVisible()
    await page.getByText('TSV Musterstadt').click()
    await expect(page.getByText('Mails zu dieser Zuordnung gehen an:')).toContainText('persönliche Adresse')
    await page.getByLabel('Amts- oder Vereinsadresse (optional)').fill('vorsitz@tsv.test')
    await page.getByRole('button', { name: 'Speichern' }).click()

    await expect(snackbar(page)).toContainText('Bitte bestätige die Amtsadresse über den Link, den wir an vorsitz@tsv.test geschickt haben.')
    expect(api.lastBody('PUT', '/api/users/me/contact-assignments/my-1/email')).toEqual({ contextEmail: 'vorsitz@tsv.test', contextEmailVisibility: 1 })
    await expect(page.getByRole('link', { name: 'Zur Organisation' })).toHaveAttribute('href', `/clubs/${data.ids.club}`)
  })

  test('unbestätigte Amtsadresse: Mails gehen weiter an die persönliche Adresse', async ({ page, api }) => {
    api.get('/api/users/me/contact-assignments', [data.myAssignment({ contextEmail: 'vorstand@tsv.test', contextEmailVerified: false })])
    await page.goto('/konto?tab=zuordnungen')
    await expect(page.getByText('Bestätigung ausstehend')).toBeVisible()
    await page.getByText('TSV Musterstadt').click()
    await expect(page.getByText('ist noch nicht bestätigt. Bis dahin gehen Mails an deine persönliche Adresse.')).toBeVisible()
  })

  test('bestätigte Amtsadresse wird als Empfänger angezeigt', async ({ page, api }) => {
    api.get('/api/users/me/contact-assignments', [data.myAssignment({ contextEmail: 'vorstand@tsv.test', contextEmailVerified: true })])
    await page.goto('/konto?tab=zuordnungen')
    await page.getByText('TSV Musterstadt').click()
    await expect(page.getByText('Mails zu dieser Zuordnung gehen an:')).toContainText('vorstand@tsv.test')
    await expect(page.getByText('Gibst du das Amt ab?')).toBeVisible()
  })

  test('Willkommensseite schlägt vor, eine Amtsadresse zu hinterlegen', async ({ page, api }) => {
    api.get('/api/auth/passkeys', [{ credentialId: 'c1', createdAt: '2026-11-01T00:00:00Z' }])
    await page.goto('/willkommen')
    await expect(page.getByText('Vereins- oder Amtsadresse hinterlegen?')).toBeVisible()
    await page.getByRole('link', { name: 'Amtsadresse hinterlegen' }).click()
    await expect(page).toHaveURL(/\/konto\?tab=zuordnungen$/)
  })

  test('ohne Zuordnungen erscheint ein Hinweis', async ({ page, api }) => {
    api.get('/api/users/me/contact-assignments', [])
    await page.goto('/konto?tab=zuordnungen')
    await expect(page.getByText('Keine Zuordnungen')).toBeVisible()
  })

  test('Passkey löschen erst nach Bestätigung', async ({ page, api }) => {
    api.delete('/api/auth/passkeys/:id', null)
    await page.goto('/konto?tab=sicherheit')
    await expect(page.getByText('MacBook', { exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'Passkey MacBook löschen' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Abbrechen' }).click()
    expect(api.callsTo('DELETE', /\/api\/auth\/passkeys\//)).toHaveLength(0)

    await page.getByRole('button', { name: 'Passkey MacBook löschen' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Löschen' }).click()
    await expect(snackbar(page)).toContainText('Passkey wurde entfernt.')
    expect(api.callsTo('DELETE', '/api/auth/passkeys/cred-1')).toHaveLength(1)
  })

  test('Auf allen Geräten abmelden', async ({ page, api }) => {
    api.post('/api/auth/logout-all', null)
    await page.goto('/konto?tab=sicherheit')
    await page.getByRole('button', { name: 'Auf allen Geräten abmelden' }).click()
    api.get('/api/auth/status', { isAuthenticated: false })
    await page.getByRole('dialog').getByRole('button', { name: 'Abmelden' }).click()
    await expect(page).toHaveURL(/\/login$/)
    expect(api.callsTo('POST', '/api/auth/logout-all')).toHaveLength(1)
  })

  test('Konto löschen verlangt die eigene E-Mail-Adresse', async ({ page, api }) => {
    api.post('/api/gdpr/delete-account', null)
    await page.goto('/konto?tab=datenschutz')
    await page.getByRole('button', { name: 'Konto löschen' }).click()
    const confirm = page.getByRole('button', { name: 'Endgültig löschen' })
    await expect(confirm).toBeDisabled()
    await page.getByLabel('E-Mail-Adresse bestätigen').fill('ERIKA@example.test')
    await expect(confirm).toBeEnabled()
    api.get('/api/auth/status', { isAuthenticated: false })
    await confirm.click()
    await expect(page).toHaveURL(/\/login$/)
    expect(api.lastBody('POST', '/api/gdpr/delete-account')).toEqual({ confirmEmail: 'ERIKA@example.test', reason: null })
  })
})
