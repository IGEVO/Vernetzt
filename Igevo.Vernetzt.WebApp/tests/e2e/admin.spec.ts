import { data, expect, raw, selectOption, snackbar, test } from './support/fixtures'

const { ids } = data

test.describe('Benutzerverwaltung', () => {
  const users = () => [
    data.admin({ passkeyCount: 1, lastLoginAt: '2026-11-04T08:00:00Z' }),
    data.user({ id: 'u-otto', firstName: 'Otto', lastName: 'Ohne', email: 'otto@example.test', passkeyCount: 0 }),
    data.user({ id: 'u-mia', firstName: 'Mia', lastName: 'Mit', email: 'mia@example.test', claims: [2], passkeyCount: 2 })
  ]

  test.beforeEach(async ({ api, signIn }) => {
    await signIn(data.admin())
    api.get('/api/users', users())
  })

  test('Passkey-Status und Zurücksetzen nur bei vorhandenen Passkeys', async ({ page, api }) => {
    api.delete('/api/users/:id/passkeys', { removedPasskeys: 2 })
    await page.goto('/admin/users')

    const row = (name: string) => page.getByRole('row').filter({ hasText: name })
    await expect(row('Mia Mit').getByTitle('2 Passkey(s) eingerichtet')).toBeVisible()
    await expect(row('Otto Ohne').getByTitle('Kein Passkey eingerichtet')).toBeVisible()
    await expect(row('Otto Ohne').getByRole('button', { name: /Passkeys .* zurücksetzen/ })).toHaveCount(0)
    // Own passkeys are managed under "Mein Konto".
    await expect(row('Ada Admin').getByRole('button', { name: /Passkeys .* zurücksetzen/ })).toHaveCount(0)

    await row('Mia Mit').getByRole('button', { name: 'Passkeys von Mia Mit zurücksetzen' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Zurücksetzen' }).click()
    await expect(snackbar(page)).toContainText('Passkeys von Mia Mit wurden zurückgesetzt.')
    expect(api.callsTo('DELETE', '/api/users/u-mia/passkeys')).toHaveLength(1)
  })

  test('Suche filtert die Tabelle', async ({ page }) => {
    await page.goto('/admin/users')
    await page.getByRole('textbox', { name: 'Benutzer suchen' }).fill('mia')
    await expect(page.getByRole('row').filter({ hasText: 'Mia Mit' })).toBeVisible()
    await expect(page.getByRole('row').filter({ hasText: 'Otto Ohne' })).toHaveCount(0)
  })

  test('Einladung mit Vereinszuordnung und Rolle', async ({ page, api }) => {
    api
      .get('/api/clubs', [data.club(), data.club({ id: ids.club2, name: 'Musikverein Harmonie' })])
      .get('/api/departments', [data.department(), data.department({ id: ids.group, name: 'Bambini', isGroup: true, parentDepartmentId: ids.department, parentDepartmentName: 'Fußball' })])
      .post('/api/invitations/invite', { success: true, message: 'OK', isExistingUser: false })
    await page.goto('/admin/users')
    await page.getByRole('button', { name: 'Person einladen' }).click()
    const dialog = page.getByRole('dialog')

    await dialog.getByLabel('E-Mail-Adresse *').fill('neu@example.test')
    await dialog.getByRole('button', { name: 'Abteilung oder Gruppe' }).click()
    await selectOption(page, dialog, 'Verein *', 'TSV Musterstadt')
    await selectOption(page, dialog, 'Abteilung oder Gruppe *', 'Fußball › Bambini')
    await selectOption(page, dialog, 'Position (optional)', 'Vorsitz')
    await dialog.getByLabel('Zuordnungsdetails (optional)').fill('Herren')
    await dialog.getByRole('checkbox', { name: /^Manager/ }).check()
    await dialog.getByRole('button', { name: 'Einladung senden' }).click()

    await expect(snackbar(page)).toContainText('Einladung an neu@example.test wurde versendet.')
    expect(api.lastBody('POST', '/api/invitations/invite')).toEqual({
      email: 'neu@example.test', clubId: null, departmentId: ids.group, contactPositionId: 'pos-chair',
      canEdit: false, assignmentDetails: 'Herren', contextEmail: null, pendingClaims: [1]
    })
    expect(api.callsTo('GET', '/api/departments')[0]?.query.get('clubId')).toBe(ids.club)
    expect(api.callsTo('GET', '/api/departments')[0]?.query.get('includeGroups')).toBe('true')
  })

  test('Manager können keine Systemadministratoren einladen', async ({ page, api, signIn }) => {
    await signIn(data.manager())
    api.get('/api/users', users())
    await page.goto('/admin/users')
    await page.getByRole('button', { name: 'Person einladen' }).click()
    await expect(page.getByRole('dialog').getByRole('checkbox', { name: /^Manager/ })).toBeVisible()
    await expect(page.getByRole('dialog').getByRole('checkbox', { name: /^Systemadministrator/ })).toHaveCount(0)
  })

  test('Rolle im Bearbeiten-Dialog vergeben', async ({ page, api }) => {
    api.post('/api/users/:id/claims', null)
    await page.goto('/admin/users')
    await page.getByRole('button', { name: 'Otto Ohne bearbeiten' }).click()
    await page.getByRole('dialog').getByText('Leserechte').click()
    await expect(snackbar(page)).toContainText('Rollen wurden aktualisiert.')
    expect(api.lastBody('POST', '/api/users/u-otto/claims')).toEqual({ claim: 2 })
  })

  test('Benutzer löschen nach Bestätigung', async ({ page, api }) => {
    api.delete('/api/users/:id', raw(204))
    await page.goto('/admin/users')
    await page.getByRole('button', { name: 'Otto Ohne löschen' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Löschen' }).click()
    await expect(snackbar(page)).toContainText('Benutzer wurde gelöscht.')
    expect(api.callsTo('DELETE', '/api/users/u-otto')).toHaveLength(1)
  })
})

test.describe('Audit-Log', () => {
  const entries = [
    { id: 'l1', entityType: 'Club', entityId: ids.club, entityDisplayName: 'TSV Musterstadt', action: 1, userId: ids.admin, userName: 'Ada Admin', timestamp: '2026-11-04T09:12:00Z', ipAddress: '10.0.0.4', oldValues: '{"City":"Alt"}', newValues: '{"City":"Musterstadt"}', notes: null },
    { id: 'l2', entityType: 'EmailRelayToken', entityId: 'x', entityDisplayName: 'Relay-Token (a1b2c3d4e5f6a7b8c9d0@relay.example)', action: 0, userId: ids.admin, userName: 'Ada Admin', timestamp: '2026-11-04T08:00:00Z', ipAddress: null, notes: 'Weiterleitung an a1b2c3d4e5f6a7b8c9d0@relay.example' },
    { id: 'l3', entityType: 'User', entityId: 'u2', entityDisplayName: 'Otto Ohne', action: 8, userId: '00000000-0000-0000-0000-000000000000', userName: null, timestamp: '2026-11-03T22:00:00Z', ipAddress: null }
  ]

  test.beforeEach(async ({ api, signIn }) => {
    await signIn(data.admin())
    api.get('/api/audit-logs', data.paged(entries, 1, 50)).get('/api/audit-logs/entity-types', ['Club', 'User'])
  })

  test('Übersicht mit lesbaren Namen, ohne Relay-Adressen', async ({ page }) => {
    await page.goto('/admin/audit-log')
    await expect(page.getByText('1–3 von 3 Einträgen')).toBeVisible()
    const row = (text: string) => page.getByRole('row').filter({ hasText: text })
    await expect(row('TSV Musterstadt')).toContainText('Geändert')
    await expect(row('Otto Ohne')).toContainText('Anmeldung fehlgeschlagen')
    await expect(row('Otto Ohne')).toContainText('System')
    await expect(row('Relay-Token')).toContainText('Weiterleitung an [Relay-Adresse]')
    await expect(page.getByText('a1b2c3d4e5f6a7b8c9d0@relay.example')).toHaveCount(0)
  })

  test('Filter werden an die API übergeben und lassen sich zurücksetzen', async ({ page, api }) => {
    await page.goto('/admin/audit-log')
    await selectOption(page, page, 'Aktion', 'Gelöscht')
    await selectOption(page, page, 'Entitätstyp', 'Club')
    await page.getByLabel('Lesezugriffe').check()
    await expect.poll(() => api.callsTo('GET', '/api/audit-logs').at(-1)?.query.toString()).toContain('includeReadAccess=true')
    const query = api.callsTo('GET', '/api/audit-logs').at(-1)!.query
    expect([query.get('action'), query.get('entityType'), query.get('page')]).toEqual(['2', 'Club', '1'])

    // Unfiltered results come from the query cache, so check the UI instead of a new request.
    const reset = page.getByRole('button', { name: 'Zurücksetzen' })
    await reset.click()
    await expect(reset).toBeDisabled()
    await expect(page.getByLabel('Lesezugriffe')).not.toBeChecked()
  })

  test('Details zeigen alten und neuen Wert formatiert', async ({ page }) => {
    await page.goto('/admin/audit-log')
    await page.getByRole('row').filter({ hasText: 'TSV Musterstadt' }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByText('Alter Wert')).toBeVisible()
    await expect(dialog.locator('pre').first()).toHaveText('{\n  "City": "Alt"\n}')
    await expect(dialog.locator('pre').last()).toHaveText('{\n  "City": "Musterstadt"\n}')
    await expect(dialog.getByText('10.0.0.4')).toBeVisible()
  })
})

test.describe('Systemeinstellungen', () => {
  const backupStatus = (overrides: Record<string, unknown> = {}) => ({
    scheduleEnabled: true,
    scheduleTime: '03:00',
    timeZone: 'Europe/Berlin',
    nextRunUtc: '2026-11-06T02:00:00Z',
    keepDaily: 14,
    keepWeekly: 8,
    beforeMigration: true,
    directory: '/app/backups',
    lastFailureUtc: null,
    lastError: null,
    backups: [{ fileName: 'vernetzt-20261105-020000-scheduled.db', createdUtc: '2026-11-05T02:00:00Z', sizeBytes: 1468006, trigger: 0 }],
    ...overrides
  })

  test.beforeEach(async ({ api, signIn }) => {
    await signIn(data.admin())
    api
      .get('/api/settings', { contactName: 'Ada', contactEmail: '', privacyPolicyUrl: '' })
      .get('/api/calendar-feeds/tokens', [data.feedToken()])
      .get('/api/backups', backupStatus())
  })

  test('Datensicherung: Status anzeigen und manuell sichern', async ({ page, api }) => {
    api.post('/api/backups', { fileName: 'vernetzt-20261105-150000-manual.db', createdUtc: '2026-11-05T15:00:00Z', sizeBytes: 1468006, trigger: 1 })
    await page.goto('/admin/settings')

    const card = page.locator('.v-card').filter({ hasText: 'Datensicherung' })
    await expect(card.getByText('täglich um 03:00 Uhr')).toBeVisible()
    await expect(card.getByText('14 Tage täglich, 8 Wochen wöchentlich')).toBeVisible()
    await expect(card.getByRole('row').filter({ hasText: 'Automatisch' })).toContainText('1,4 MB')

    await card.getByRole('button', { name: 'Jetzt sichern' }).click()
    await expect(snackbar(page)).toContainText('Sicherung vernetzt-20261105-150000-manual.db wurde erstellt.')
    expect(api.callsTo('POST', '/api/backups')).toHaveLength(1)
  })

  test('Datensicherung: zeigt einen Fehler der letzten Sicherung', async ({ page, api }) => {
    api.get('/api/backups', backupStatus({ lastFailureUtc: '2026-11-05T10:00:00Z', lastError: 'No space left on device' }))
    await page.goto('/admin/settings')
    await expect(page.getByText('Letzte Sicherung fehlgeschlagen')).toBeVisible()
    await expect(page.getByText('No space left on device')).toBeVisible()
  })

  test('warnt bei fehlenden Pflichtangaben und speichert', async ({ page, api }) => {
    api.put('/api/settings', null)
    await page.goto('/admin/settings')
    await expect(page.getByText('Solange nicht alle drei Felder ausgefüllt sind')).toBeVisible()
    await page.getByLabel('E-Mail-Adresse *').fill('info@example.test')
    await page.getByLabel('URL der Datenschutzerklärung *').fill('https://example.test/datenschutz')
    await expect(page.getByText('Solange nicht alle drei Felder ausgefüllt sind')).toHaveCount(0)
    await page.getByRole('button', { name: 'Speichern' }).click()
    await expect(snackbar(page)).toContainText('Einstellungen wurden gespeichert.')
    expect(api.lastBody('PUT', '/api/settings')).toMatchObject({ contactName: 'Ada', contactEmail: 'info@example.test', privacyPolicyUrl: 'https://example.test/datenschutz' })
  })

  test('Kalender-Links: Erneuern und Widerrufen erst nach Rückfrage', async ({ page, api }) => {
    api.post('/api/calendar-feeds/tokens/:id/rotate', null).delete('/api/calendar-feeds/tokens/:id', null)
    await page.goto('/admin/settings')

    const main = page.getByRole('main')
    await main.getByRole('button', { name: 'Erneuern' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Abbrechen' }).click()
    expect(api.callsTo('POST', '/api/calendar-feeds/tokens/feed-1/rotate')).toHaveLength(0)

    await main.getByRole('button', { name: 'Erneuern' }).click()
    await page.getByRole('dialog').filter({ hasText: 'Kalender-Link erneuern?' }).getByRole('button', { name: 'Erneuern' }).click()
    await expect(snackbar(page)).toContainText('Kalender-Link wurde erneuert.')
    expect(api.callsTo('POST', '/api/calendar-feeds/tokens/feed-1/rotate')).toHaveLength(1)

    await main.getByRole('button', { name: 'Widerrufen' }).click()
    await page.getByRole('dialog').filter({ hasText: 'Kalender-Link widerrufen?' }).getByRole('button', { name: 'Widerrufen' }).click()
    expect(api.callsTo('DELETE', '/api/calendar-feeds/tokens/feed-1')).toHaveLength(1)
  })

  test('Kalender-Link kopieren', async ({ page, context, browserName }) => {
    test.skip(browserName !== 'chromium', 'Clipboard-Berechtigungen nur in Chromium')
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    await page.goto('/admin/settings')
    await page.getByRole('button', { name: 'Kopieren' }).click()
    await expect(page.getByRole('button', { name: 'Kopiert' })).toBeVisible()
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(data.feedToken().feedUrl)
  })

  test('fehlenden Link anlegen', async ({ page, api }) => {
    api.post('/api/calendar-feeds/tokens', { token: data.feedToken({ id: 'feed-2', highlightsOnly: true }), feedUrl: 'x' })
    await page.goto('/admin/settings')
    await page.getByRole('button', { name: 'Link erstellen' }).click()
    expect(api.lastBody('POST', '/api/calendar-feeds/tokens')).toEqual({ scope: 2, includeDepartments: true, highlightsOnly: true })
  })
})

test.describe('Stammdaten', () => {
  test('Vereinstyp anlegen', async ({ page, api, signIn }) => {
    await signIn(data.admin())
    api.get('/api/clubtypes', data.clubTypes).post('/api/clubtypes', ({ body }) => ({ id: 'ct-new', ...(body as object) }))
    await page.goto('/admin/club-types')
    await expect(page.getByRole('row').filter({ hasText: 'Sportverein' })).toBeVisible()
    await page.getByRole('button', { name: 'Hinzufügen' }).click()
    await page.getByRole('dialog').getByLabel('Name').fill('Reitverein')
    await page.getByRole('dialog').getByLabel('Name').press('Enter')
    await expect(snackbar(page)).toContainText('Eintrag wurde gespeichert.')
    expect(api.lastBody('POST', '/api/clubtypes')).toEqual({ name: 'Reitverein', description: '', isActive: true })
  })
})

test.describe('Ferien & Feiertage', () => {
  const year = new Date().getFullYear()
  const holiday = (overrides: Record<string, unknown> = {}) => ({ id: 'h1', title: 'Kirmes', startDate: `${year}-08-22`, endDate: `${year}-08-24`, sourceId: null, sourceName: null, ...overrides })
  const source = (overrides: Record<string, unknown> = {}) => ({ id: 's1', name: 'Schulferien NRW', url: 'https://example.org/nrw.ics', isEnabled: true, lastSyncedAt: '2026-11-07T03:00:00Z', lastSyncError: null, entryCount: 12, ...overrides })

  test.beforeEach(async ({ api, signIn }) => {
    await signIn(data.manager())
    api
      .get('/api/holidays', [holiday(), holiday({ id: 'h2', title: 'Herbstferien', startDate: `${year}-10-12`, endDate: `${year}-10-24`, sourceId: 's1', sourceName: 'Schulferien NRW' })])
      .get('/api/holidays/sources', [source()])
  })

  test('manuellen Eintrag anlegen; importierte sind schreibgeschützt', async ({ page, api }) => {
    api.post('/api/holidays', ({ body }) => ({ id: 'h-new', ...(body as object) }))
    await page.goto('/admin/holidays')
    await expect(page.getByRole('row').filter({ hasText: 'Kirmes' }).getByRole('button', { name: 'Kirmes bearbeiten' })).toBeVisible()
    await expect(page.getByRole('row').filter({ hasText: 'Herbstferien' }).getByRole('button')).toHaveCount(0)

    await page.getByRole('button', { name: 'Eintrag hinzufügen' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Name *').fill('Brückentag')
    await dialog.getByLabel('Von *').fill(`${year}-10-02`)
    await dialog.getByRole('button', { name: 'Speichern' }).click()

    await expect(snackbar(page)).toContainText('Eintrag wurde gespeichert.')
    expect(api.lastBody('POST', '/api/holidays')).toEqual({ title: 'Brückentag', startDate: `${year}-10-02`, endDate: `${year}-10-02` })
  })

  test('Abonnement anlegen meldet fehlgeschlagenen Abgleich', async ({ page, api }) => {
    api.post('/api/holidays/sources', ({ body }) => source({ id: 's2', ...(body as object), entryCount: 0, lastSyncedAt: null, lastSyncError: 'Der Server antwortete mit Status 404.' }))
    await page.goto('/admin/holidays')
    await page.getByRole('tab', { name: 'Abonnements' }).click()
    await expect(page.getByRole('row').filter({ hasText: 'Schulferien NRW' })).toBeVisible()

    await page.getByRole('button', { name: 'Abonnement hinzufügen' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Name *').fill('Feiertage NRW')
    await dialog.getByLabel('iCal-Adresse *').fill('webcal://example.org/feiertage.ics')
    await dialog.getByRole('button', { name: 'Speichern' }).click()

    await expect(snackbar(page)).toContainText('Abgleich fehlgeschlagen: Der Server antwortete mit Status 404.')
    expect(api.lastBody('POST', '/api/holidays/sources')).toEqual({ name: 'Feiertage NRW', url: 'webcal://example.org/feiertage.ics', isEnabled: true })
  })
})
