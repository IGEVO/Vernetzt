import { data, expect, expectAccessible, fail, raw, test } from './support/fixtures'

test.describe('Anmeldung', () => {
  test('ungültige E-Mail wird beim Absenden mit Enter gemeldet, ohne Anfrage zu senden', async ({ page, api }) => {
    await page.goto('/login')
    await page.getByLabel('E-Mail-Adresse').fill('keine-mail')
    await page.getByLabel('E-Mail-Adresse').press('Enter')
    await expect(page.getByText('Bitte eine gültige E-Mail-Adresse eingeben')).toBeVisible()
    expect(api.callsTo('POST', '/api/auth/login-link/request')).toHaveLength(0)
  })

  test('Anmeldelink anfordern und andere Adresse verwenden', async ({ page, api }) => {
    api.post('/api/auth/login-link/request', null)
    await page.goto('/login?returnUrl=%2Fclubs')
    await page.getByLabel('E-Mail-Adresse').fill('erika@example.test')
    await page.getByRole('button', { name: 'Anmeldelink senden' }).click()

    await expect(page.getByText('Wir haben dir einen Anmeldelink an erika@example.test gesendet')).toBeVisible()
    expect(api.lastBody('POST', '/api/auth/login-link/request')).toEqual({ email: 'erika@example.test', returnUrl: '/clubs' })

    await page.getByRole('button', { name: 'Andere E-Mail-Adresse verwenden' }).click()
    await expect(page.getByLabel('E-Mail-Adresse')).toBeVisible()
  })

  test('Fehler beim Versand wird angezeigt', async ({ page, api }) => {
    api.post('/api/auth/login-link/request', fail(500, 'SMTP nicht erreichbar'))
    await page.goto('/login')
    await page.getByLabel('E-Mail-Adresse').fill('erika@example.test')
    await page.getByRole('button', { name: 'Anmeldelink senden' }).click()
    await expect(page.getByText('Der Anmeldelink konnte nicht gesendet werden')).toBeVisible()
  })

  test('geschützte Seite leitet zur Anmeldung um und nach dem Anmeldelink zurück', async ({ page, api }) => {
    let signedIn = false
    const user = data.user()
    api
      .get('/api/auth/status', () => ({ isAuthenticated: signedIn, hasPasskey: true }))
      .get('/api/users/me', user)
      .post('/api/auth/login-link/verify', () => { signedIn = true; return null })
      .get('/api/clubs/search', data.paged([data.clubSummary()]))

    await page.goto('/clubs')
    await expect(page).toHaveURL(/\/login\?returnUrl=(%2F|\/)clubs$/)

    await page.goto(`/login?uid=${user.id}&token=t0k3n&returnUrl=%2Fclubs`)
    await expect(page.getByRole('heading', { name: 'Vereine', level: 1 })).toBeVisible()
    expect(api.lastBody('POST', '/api/auth/login-link/verify')).toEqual({ userId: user.id, token: 't0k3n' })
  })

  test('zu viele Versuche werden verständlich gemeldet', async ({ page, api }) => {
    api.post('/api/auth/login-link/verify', raw(429))
    await page.goto('/login?uid=x&token=gültig')
    await expect(page.getByText('Zu viele Anmeldeversuche in kurzer Zeit')).toBeVisible()
  })

  test('abgelaufener Anmeldelink zeigt einen Hinweis', async ({ page, api }) => {
    api.post('/api/auth/login-link/verify', fail(400, 'Ungültig'))
    await page.goto('/login?uid=x&token=alt')
    await expect(page.getByText('Der Anmeldelink ist ungültig, abgelaufen oder wurde bereits verwendet')).toBeVisible()
  })

  test('Abmelden über das Benutzermenü', async ({ page, api, signIn }) => {
    await signIn()
    api.post('/api/auth/logout', null)
    await page.goto('/')
    await page.getByRole('button', { name: 'Benutzermenü' }).click()
    await expect(page.getByText('erika@example.test')).toBeVisible()
    api.get('/api/auth/status', { isAuthenticated: false })
    await page.getByRole('listitem').filter({ hasText: 'Abmelden' }).click()
    await expect(page).toHaveURL(/\/login$/)
    expect(api.callsTo('POST', '/api/auth/logout')).toHaveLength(1)
  })
})

test.describe('Onboarding', () => {
  const invitation = { email: 'neu@example.test', expiresAt: '2026-12-01T00:00:00Z', privacyPolicyUrl: 'https://example.test/datenschutz', pendingAssignments: [{ invitationId: 'inv-1', clubName: 'TSV Musterstadt', positionName: 'Vorsitz', contextEmail: null }] }

  test('ungültige Einladung', async ({ page, api }) => {
    api.get('/api/invitations/details', fail(404, 'Nicht gefunden'))
    await page.goto('/onboarding?token=kaputt')
    await expect(page.getByText('Die Einladung ist ungültig oder abgelaufen.')).toBeVisible()
  })

  test('Registrierung abschließen', async ({ page, api }) => {
    api.get('/api/invitations/details', invitation).post('/api/invitations/accept', { success: true, magicLinkSent: true })
    await page.goto('/onboarding?token=abc')

    await expect(page.getByText('Einladung für neu@example.test')).toBeVisible()
    await expect(page.getByText('TSV Musterstadt')).toBeVisible()
    const submit = page.getByRole('button', { name: 'Registrierung abschließen' })
    await expect(submit).toBeDisabled()

    await page.getByLabel('Vorname').fill('Nora')
    await page.getByLabel('Nachname').fill('Neu')
    await page.getByRole('radio', { name: /Mit Anmeldelink per E-Mail/ }).check()
    await page.getByRole('checkbox', { name: /Ich akzeptiere/ }).check()
    await submit.click()

    await expect(page.getByText('Fast geschafft!')).toBeVisible()
    expect(api.lastBody('POST', '/api/invitations/accept')).toMatchObject({ token: 'abc', firstName: 'Nora', lastName: 'Neu', acceptedPrivacyPolicy: true, preferredLoginMethod: 0, phoneNumber: null, assignmentContextEmails: { 'inv-1': '' } })
  })

  test('Registrierung mit Amtsadresse für die Zuordnung', async ({ page, api }) => {
    const suggested = { ...invitation, pendingAssignments: [{ ...invitation.pendingAssignments[0], contextEmail: 'vorstand@tsv.test' }] }
    api.get('/api/invitations/details', suggested).post('/api/invitations/accept', { success: true, magicLinkSent: true })
    await page.goto('/onboarding?token=abc')

    await expect(page.getByText('Diese Adresse ist dein persönlicher Zugang zu Vernetzt.')).toBeVisible()
    await expect(page.getByLabel('Amtsadresse für diese Zuordnung (optional)')).toHaveValue('vorstand@tsv.test')
    await page.getByLabel('Amtsadresse für diese Zuordnung (optional)').fill('kasse@tsv.test')
    await page.getByLabel('Vorname').fill('Nora')
    await page.getByLabel('Nachname').fill('Neu')
    await page.getByRole('checkbox', { name: /Ich akzeptiere/ }).check()
    await page.getByRole('button', { name: 'Registrierung abschließen' }).click()

    await expect(page.getByText('An jede angegebene Amtsadresse schicken wir zusätzlich eine Bestätigungs-E-Mail.')).toBeVisible()
    expect(api.lastBody('POST', '/api/invitations/accept')).toMatchObject({ assignmentContextEmails: { 'inv-1': 'kasse@tsv.test' } })
  })

  test('Registrierung warnt, wenn die Einladung an eine Amtsadresse ging', async ({ page, api }) => {
    api.get('/api/invitations/details', { ...invitation, email: 'vorstand@tsv.test' })
    await page.goto('/onboarding?token=abc')
    await expect(page.getByText('sieht nach einer Vereins- oder Amtsadresse aus')).toBeVisible()
    await expect(page.getByLabel('Amtsadresse für diese Zuordnung (optional)')).toBeVisible()
  })

  test('Onboarding ist zentriert und barrierearm', async ({ page, api }) => {
    api.get('/api/invitations/details', invitation)
    await page.goto('/onboarding?token=abc')
    const card = await page.locator('.v-card').first().boundingBox()
    const viewport = page.viewportSize()!
    expect(Math.abs(card!.x + card!.width / 2 - viewport.width / 2)).toBeLessThan(3)
    await expectAccessible(page, '.v-card')
  })
})

test.describe('Navigation', () => {
  test('Darstellung lässt sich umschalten und bleibt gespeichert', async ({ page, signIn }) => {
    await signIn()
    await page.goto('/')
    await page.getByRole('button', { name: 'Darstellung auswählen' }).click()
    await page.getByRole('option', { name: 'Dunkel' }).or(page.getByRole('listitem').filter({ hasText: 'Dunkel' })).first().click()
    await expect(page.locator('.v-theme--vernetztDark').first()).toBeVisible()
    await page.reload()
    await expect(page.locator('.v-theme--vernetztDark').first()).toBeVisible()
  })

  test('Administration ist für normale Benutzer weder sichtbar noch erreichbar', async ({ page, signIn }) => {
    await signIn()
    await page.goto('/')
    await expect(page.getByText('Administration')).toHaveCount(0)
    await page.goto('/admin/users')
    await expect(page.getByText('Zugriff verweigert')).toBeVisible()
  })

  test('Seitentitel wird nach Navigation fokussiert, ohne sichtbaren Rahmen', async ({ page, api, signIn }) => {
    await signIn()
    api.get('/api/clubs/search', data.paged([data.clubSummary()]))
    await page.goto('/')
    await page.getByRole('link', { name: 'Vereine' }).first().click()
    const heading = page.getByRole('heading', { name: 'Vereine', level: 1 })
    await expect(heading).toBeFocused()
    expect(await heading.evaluate((element) => getComputedStyle(element).outlineStyle)).toBe('none')
  })
})
