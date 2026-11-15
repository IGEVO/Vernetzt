import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

test('Anmeldeseite ist responsiv und barrierearm', async ({ page }) => {
  await page.addInitScript(() => { Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => false }) })
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { success: true, data: { isAuthenticated: false } } }))
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Vernetzt' })).toBeVisible()
  await expect(page.getByLabel('E-Mail-Adresse')).toBeVisible()
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((item) => ['critical', 'serious'].includes(item.impact ?? ''))).toEqual([])
})

test('Anmeldedialog ist im Dark Mode zentriert und kontrastreich', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'chromium', 'Die Layout- und Kontrastprüfung läuft einmal in Chromium.')
  await page.addInitScript(() => {
    localStorage.setItem('themePreference', 'Dark')
    Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => false })
  })
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { success: true, data: { isAuthenticated: false } } }))
  await page.goto('/login')

  const card = await page.locator('.v-card').first().boundingBox()
  const viewport = page.viewportSize()
  expect(card).not.toBeNull()
  expect(viewport).not.toBeNull()
  expect(Math.abs((card!.x + card!.width / 2) - viewport!.width / 2)).toBeLessThan(3)
  expect(Math.abs((card!.y + card!.height / 2) - viewport!.height / 2)).toBeLessThan(3)

  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((item) => ['critical', 'serious'].includes(item.impact ?? ''))).toEqual([])
})

test('Passkey-Anmeldung verwendet WebAuthn ohne Token im Browser-Speicher', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'chromium', 'Der WebAuthn-Mock läuft einmal im Chromium-Smoke-Test.')
  await page.addInitScript(() => {
    Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => false })
    Object.defineProperty(PublicKeyCredential, 'parseRequestOptionsFromJSON', { value: (value: unknown) => value })
    Object.defineProperty(navigator.credentials, 'get', { value: async () => ({
      id: 'credential-id', rawId: new Uint8Array([1, 2, 3]).buffer, type: 'public-key',
      response: { authenticatorData: new Uint8Array([1]).buffer, clientDataJSON: new Uint8Array([2]).buffer, signature: new Uint8Array([3]).buffer, userHandle: null },
      getClientExtensionResults: () => ({})
    }) })
  })
  let signInBody = ''
  await page.route('**/api/auth/passkeys/request-options', (route) => route.fulfill({ json: { success: true, data: { challenge: 'AQ', allowCredentials: [] } } }))
  await page.route('**/api/auth/passkeys/signin', async (route) => { signInBody = route.request().postData() ?? ''; await route.fulfill({ json: { success: true, data: { message: 'Angemeldet.' } } }) })
  await page.route('**/api/auth/passkeys', (route) => route.fulfill({ json: { success: true, data: [{ credentialId: 'AQID', name: 'Testgerät', createdAt: '2026-10-28T08:00:00Z' }] } }))
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { success: true, data: { isAuthenticated: true } } }))
  await page.route('**/api/users/me', (route) => route.fulfill({ json: { success: true, data: { id: '00000000-0000-0000-0000-000000000001', email: 'user@example.test', firstName: 'Erika', lastName: 'Muster', emailVisibility: 1, phoneVisibility: 1, emailVerified: true, hasAvatar: false, claims: [], createdAt: '2026-10-28T08:00:00Z' } } }))
  await page.route('**/api/stats/dashboard', (route) => route.fulfill({ json: { success: true, data: { userDisplayName: 'Erika Muster', myClubs: [] } } }))

  await page.goto('/login')
  await page.getByRole('button', { name: 'Mit Passkey anmelden' }).click()
  await expect(page).toHaveURL(/\/$/)
  expect(signInBody).toContain('credentialJson')
  expect(await page.evaluate(() => Object.keys(localStorage))).not.toContain('jwt')
  expect(await page.evaluate(() => Object.keys(localStorage))).not.toContain('refresh_token')
})

test('Anmeldelink aus dem Onboarding führt zur Passkey-Einrichtung auf der Willkommensseite', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'chromium', 'Der Login-Link-Ablauf läuft einmal im Chromium-Smoke-Test.')
  await page.addInitScript(() => { Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => false }) })
  let verifyBody = ''
  let signedIn = false
  await page.route('**/api/auth/login-link/verify', async (route) => { verifyBody = route.request().postData() ?? ''; signedIn = true; await route.fulfill({ json: { success: true, data: { message: 'Angemeldet.' } } }) })
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { success: true, data: { isAuthenticated: signedIn } } }))
  await page.route('**/api/users/me', (route) => route.fulfill({ json: { success: true, data: { id: '00000000-0000-0000-0000-000000000002', email: 'neu@example.test', firstName: 'Nora', lastName: 'Neu', emailVisibility: 1, phoneVisibility: 1, emailVerified: true, hasAvatar: false, claims: [], createdAt: '2026-11-04T08:00:00Z' } } }))
  await page.route('**/api/auth/passkeys', (route) => route.fulfill({ json: { success: true, data: [] } }))

  const returnUrl = encodeURIComponent('/willkommen?einrichten=passkey')
  await page.goto(`/login?uid=00000000-0000-0000-0000-000000000002&token=abc%2B123&returnUrl=${returnUrl}`)

  await expect(page).toHaveURL(/\/willkommen\?einrichten=passkey$/)
  expect(JSON.parse(verifyBody)).toEqual({ userId: '00000000-0000-0000-0000-000000000002', token: 'abc+123' })
  await expect(page.getByRole('heading', { name: 'Willkommen, Nora!' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Passkey jetzt einrichten' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Später' })).toBeVisible()
  // Scoped to the new passkey card: the main navigation and the global .page-title colour have known, pre-existing findings.
  const results = await new AxeBuilder({ page }).include('.v-main .v-card').analyze()
  expect(results.violations.filter((item) => ['critical', 'serious'].includes(item.impact ?? ''))).toEqual([])
})

test('Admin mit Anmeldelink muss sich mit Passkey anmelden, bevor Admin-Funktionen erreichbar sind', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'chromium', 'Der WebAuthn-Mock läuft einmal im Chromium-Smoke-Test.')
  await page.addInitScript(() => {
    Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => false })
    Object.defineProperty(PublicKeyCredential, 'parseRequestOptionsFromJSON', { value: (value: unknown) => value })
    Object.defineProperty(navigator.credentials, 'get', { value: async () => ({
      id: 'credential-id', rawId: new Uint8Array([1, 2, 3]).buffer, type: 'public-key',
      response: { authenticatorData: new Uint8Array([1]).buffer, clientDataJSON: new Uint8Array([2]).buffer, signature: new Uint8Array([3]).buffer, userHandle: null },
      getClientExtensionResults: () => ({})
    }) })
  })
  let passkeySession = false
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { success: true, data: { isAuthenticated: true, passkeyRequired: !passkeySession, hasPasskey: true } } }))
  await page.route('**/api/users/me', (route) => route.fulfill({ json: { success: true, data: { id: '00000000-0000-0000-0000-000000000003', email: 'admin@example.test', firstName: 'Ada', lastName: 'Admin', emailVisibility: 1, phoneVisibility: 1, emailVerified: true, hasAvatar: false, claims: [0], createdAt: '2026-11-04T08:00:00Z' } } }))
  await page.route('**/api/auth/passkeys/request-options', (route) => route.fulfill({ json: { success: true, data: { challenge: 'AQ', allowCredentials: [] } } }))
  await page.route('**/api/auth/passkeys/signin', async (route) => { passkeySession = true; await route.fulfill({ json: { success: true, data: { message: 'Angemeldet.' } } }) })
  await page.route('**/api/users', (route) => route.fulfill(passkeySession
    ? { json: { success: true, data: [] } }
    : { status: 403, json: { success: false, error: { code: 'PASSKEY_REQUIRED', message: 'Bitte melde dich mit deinem Passkey an.' } } }))

  await page.goto('/admin/users')
  await expect(page).toHaveURL(/\/passkey-erforderlich\?returnUrl=(%2F|\/)admin(%2F|\/)users$/)
  await expect(page.getByRole('heading', { name: 'Anmeldung mit Passkey' })).toBeVisible()
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((item) => ['critical', 'serious'].includes(item.impact ?? ''))).toEqual([])

  await page.getByRole('button', { name: 'Mit Passkey anmelden' }).click()
  await expect(page).toHaveURL(/\/admin\/users$/)
  await expect(page.getByRole('heading', { name: 'Benutzerverwaltung' })).toBeVisible()
})

test('Vereinsbereiche sind echte Tabs auf derselben Detailseite', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'chromium', 'Die Tab-Navigation läuft einmal im Chromium-Smoke-Test.')
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { success: true, data: { isAuthenticated: true } } }))
  await page.route('**/api/users/me', (route) => route.fulfill({ json: { success: true, data: { id: 'user-1', email: 'user@example.test', firstName: 'Erika', lastName: 'Muster', emailVisibility: 1, phoneVisibility: 1, emailVerified: true, hasAvatar: false, claims: [], createdAt: '2026-10-31T08:00:00Z' } } }))
  await page.route('**/api/clubs/club-1', (route) => route.fulfill({ json: { success: true, data: { id: 'club-1', name: 'FC Beispielberg', hasAvatar: false, departments: [], contactAssignments: [], canEdit: false, canDelete: false, canManageContacts: false } } }))
  await page.route('**/api/events/club/club-1/paged**', (route) => route.fulfill({ json: { success: true, data: { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false } } }))

  await page.goto('/clubs/club-1')
  const overview = page.getByRole('tab', { name: 'Übersicht' })
  const contacts = page.getByRole('tab', { name: 'Kontaktpersonen' })
  await expect(overview).toHaveAttribute('aria-selected', 'true')
  await expect(contacts).toHaveAttribute('aria-selected', 'false')

  await page.getByRole('tab', { name: 'Termine' }).click()
  await expect(page).toHaveURL(/\/clubs\/club-1\?tab=events$/)
  await expect(page.getByRole('heading', { name: 'Termine', level: 2 })).toBeVisible()
})

test('Anmeldelink kann mit Enter angefordert werden', async ({ page }) => {
  await page.addInitScript(() => { Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => false }) })
  let requestBody = ''
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { success: true, data: { isAuthenticated: false } } }))
  await page.route('**/api/auth/login-link/request', async (route) => { requestBody = route.request().postData() ?? ''; await route.fulfill({ status: 204 }) })
  await page.goto('/login')
  await page.getByLabel('E-Mail-Adresse').fill('erika@example.test')
  await page.getByLabel('E-Mail-Adresse').press('Enter')
  await expect(page.getByText('Wir haben dir einen Anmeldelink')).toBeVisible()
  expect(JSON.parse(requestBody)).toMatchObject({ email: 'erika@example.test' })
})

test('Anmeldelink kann mit Enter angefordert werden, während die Passkey-Autofill-Anfrage läuft', async ({ page }) => {
  // Real browsers keep a conditional (autofill) passkey request pending while the user types the address.
  await page.addInitScript(() => {
    Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => true })
    Object.defineProperty(navigator.credentials, 'get', { value: () => new Promise(() => {}) })
  })
  let requests = 0
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { success: true, data: { isAuthenticated: false } } }))
  await page.route('**/api/auth/passkeys/request-options', (route) => route.fulfill({ json: { success: true, data: { challenge: 'AQ', allowCredentials: [] } } }))
  await page.route('**/api/auth/login-link/request', async (route) => { requests++; await route.fulfill({ status: 204 }) })
  await page.goto('/login')
  await page.getByLabel('E-Mail-Adresse').pressSequentially('erika@example.test')
  await page.getByLabel('E-Mail-Adresse').press('Enter')
  await expect(page.getByText('Wir haben dir einen Anmeldelink')).toBeVisible()
  expect(requests).toBe(1)
})

test('Benutzerverwaltung zeigt abgelehnte Einladungen und Passkey-Status an', async ({ page }) => {
  await page.addInitScript(() => { Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => false }) })
  const admin = { id: 'a1', email: 'admin@example.test', firstName: 'Ada', lastName: 'Admin', emailVisibility: 1, phoneVisibility: 1, emailVerified: true, hasAvatar: false, claims: [0], createdAt: '2026-10-28T08:00:00Z' }
  await page.route('**/api/**', (route) => route.fulfill({ json: { success: true, data: null } }))
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { success: true, data: { isAuthenticated: true, hasPasskey: true } } }))
  await page.route('**/api/users/me', (route) => route.fulfill({ json: { success: true, data: admin } }))
  await page.route('**/api/users', (route) => route.fulfill({ json: { success: true, data: [
    { ...admin, passkeyCount: 1 },
    { ...admin, id: 'u2', email: 'ohne@example.test', firstName: 'Otto', lastName: 'Ohne', claims: [], passkeyCount: 0 },
    { ...admin, id: 'u3', email: 'mit@example.test', firstName: 'Mia', lastName: 'Mit', claims: [], passkeyCount: 2 }
  ] } }))
  await page.route('**/api/invitations/invite', (route) => route.fulfill({ json: { success: true, data: { success: false, message: 'Einladungen können erst versendet werden, wenn alle Pflichteinstellungen konfiguriert sind.', isExistingUser: false } } }))

  await page.goto('/admin/users')
  await expect(page.getByRole('button', { name: 'Passkeys von Mia Mit zurücksetzen' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Passkeys von Otto Ohne zurücksetzen' })).toHaveCount(0)

  await page.getByRole('button', { name: 'Person einladen' }).click()
  await page.getByLabel('Persönliche E-Mail-Adresse *').fill('neu@example.test')
  await page.getByRole('button', { name: 'Einladung senden' }).click()
  await expect(page.getByText('Einladungen können erst versendet werden')).toBeVisible()
  await expect(page.getByText('Einladung an neu@example.test wurde versendet.')).toHaveCount(0)
})
