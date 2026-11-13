import { data, expect, fail, selectOption, snackbar, test } from './support/fixtures'

const { ids } = data

test.describe('Vereinsübersicht', () => {
  test('zeigt Vereine und sucht auch in Abteilungen und Gruppen', async ({ page, api, signIn }) => {
    await signIn()
    api.get('/api/clubs/search', ({ query }) => query.get('searchTerm') === 'fuß'
      ? data.paged([data.clubSummary({ matchingDepartments: [{ id: ids.department, name: 'Fußball' }, { id: ids.group, name: 'Bambini', parentDepartmentName: 'Fußball' }] })])
      : data.paged([data.clubSummary(), data.clubSummary({ id: ids.club2, name: 'Musikverein Harmonie', clubTypeName: 'Musikverein', departmentCount: 0 })]))

    await page.goto('/clubs')
    await expect(page.getByText('2 Vereine')).toBeVisible()
    await expect(page.getByRole('link', { name: /TSV Musterstadt/ })).toHaveAttribute('href', `/clubs/${ids.club}`)
    await expect(page.getByText('1 Abteilung', { exact: true })).toBeVisible()

    await page.getByRole('textbox', { name: 'Vereine suchen' }).fill('fuß')
    await expect(page.getByText('Treffer in Abteilungen und Gruppen')).toBeVisible()
    await expect(page.locator('.v-chip').filter({ hasText: /^Fußball$/ })).toBeVisible()
    await expect(page.locator('.v-chip').filter({ hasText: 'Fußball › Bambini' })).toBeVisible()
    await expect(page).toHaveURL(/\?q=fu%C3%9F$/)
  })

  test('Suche ohne Treffer zeigt einen Hinweis', async ({ page, api, signIn }) => {
    await signIn()
    api.get('/api/clubs/search', ({ query }) => data.paged(query.get('searchTerm') ? [] : [data.clubSummary()]))
    await page.goto('/clubs?q=xyz')
    await expect(page.getByText('Keine Vereine gefunden')).toBeVisible()
  })

  test('normale Benutzer können keine Vereine anlegen', async ({ page, api, signIn }) => {
    await signIn()
    api.get('/api/clubs/search', data.paged([data.clubSummary()]))
    await page.goto('/clubs')
    await expect(page.getByText('TSV Musterstadt')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Verein anlegen' })).toHaveCount(0)
  })

  test('Manager legt einen Verein an und landet auf der Detailseite', async ({ page, api, signIn }) => {
    await signIn(data.manager())
    api
      .get('/api/clubs/search', data.paged([]))
      .post('/api/clubs', ({ body }) => data.club({ ...(body as object), id: 'c-new' }))
      .get('/api/clubs/c-new', data.club({ id: 'c-new', name: 'SV Neu', departments: [], contactAssignments: [] }))

    await page.goto('/clubs')
    await page.getByRole('button', { name: 'Verein anlegen' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('button', { name: 'Anlegen' }).click()
    await expect(dialog.getByText('Pflichtfeld')).toBeVisible()

    await dialog.getByLabel('Name *').fill('SV Neu')
    await selectOption(page, dialog, 'Vereinstyp', 'Sportverein')
    await dialog.getByLabel('Webseite').fill('sv-neu.de')
    await dialog.getByLabel('Name *').press('Enter')
    await expect(dialog.getByText('Bitte mit http:// oder https:// angeben')).toBeVisible()
    expect(api.callsTo('POST', '/api/clubs')).toHaveLength(0)

    await dialog.getByLabel('Webseite').fill('https://sv-neu.de')
    await dialog.getByRole('button', { name: 'Anlegen' }).click()
    await expect(page).toHaveURL(/\/clubs\/c-new$/)
    expect(api.lastBody('POST', '/api/clubs')).toMatchObject({ name: 'SV Neu', clubTypeId: 'ct-sport', websiteUrl: 'https://sv-neu.de', email: null })
  })
})

test.describe('Vereinsdetails', () => {
  test.beforeEach(async ({ api, signIn }) => {
    await signIn()
    api.get(`/api/clubs/${ids.club}`, data.club())
  })

  test('Übersicht mit Kontaktdaten, Abteilungen und Breadcrumb', async ({ page , api }) => {
    api.get('/api/clubs/search', data.paged([data.clubSummary()]))
    await page.goto(`/clubs/${ids.club}`)
    await expect(page.getByRole('heading', { name: 'TSV Musterstadt', level: 1 })).toBeVisible()
    await expect(page.getByText('Hauptstraße 1, 12345 Musterstadt')).toBeVisible()
    await expect(page.getByRole('link', { name: /info@tsv\.test/ })).toHaveAttribute('href', 'mailto:info@tsv.test')
    await expect(page.getByRole('link', { name: /Fußball/ })).toHaveAttribute('href', `/departments/${ids.department}`)
    await expect(page.getByRole('tab', { name: /Kontaktpersonen/ })).toContainText('1')
    await page.getByRole('link', { name: 'Vereine' }).last().click()
    await expect(page).toHaveURL(/\/clubs$/)
  })

  test('Kontaktdaten über die Karte bearbeiten', async ({ page, api }) => {
    api.put(`/api/clubs/${ids.club}`, ({ body }) => data.club(body as object))
    await page.goto(`/clubs/${ids.club}`)
    await page.getByRole('main').getByRole('button', { name: 'Bearbeiten' }).last().click()
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByText('Verein bearbeiten')).toBeVisible()
    await expect(dialog.getByLabel('Name *')).toHaveValue('TSV Musterstadt')
    await dialog.getByLabel('Ort', { exact: true }).fill('Neustadt')
    await dialog.getByRole('button', { name: 'Speichern' }).click()
    await expect(snackbar(page)).toContainText('Verein wurde gespeichert.')
    expect(api.lastBody('PUT', `/api/clubs/${ids.club}`)).toMatchObject({ name: 'TSV Musterstadt', city: 'Neustadt', street: 'Hauptstraße 1', websiteUrl: 'https://tsv.test' })
  })

  test('Social-Media-Links pflegen und anzeigen', async ({ page, api }) => {
    api.put(`/api/clubs/${ids.club}`, ({ body }) => data.club(body as object))
    await page.goto(`/clubs/${ids.club}`)
    await page.getByRole('main').getByRole('button', { name: 'Bearbeiten' }).last().click()
    const dialog = page.getByRole('dialog')

    await dialog.getByRole('button', { name: 'Social-Media-Link hinzufügen' }).click()
    await dialog.getByLabel('Profil-URL').fill('https://www.instagram.com/tsv')
    await expect(dialog.getByRole('combobox', { name: 'Plattform', exact: true })).toHaveValue('Instagram')

    await dialog.getByRole('button', { name: 'Social-Media-Link hinzufügen' }).click()
    await dialog.getByLabel('Profil-URL').last().fill('mastodon.example/@tsv')
    await dialog.getByRole('button', { name: 'Speichern' }).click()
    await expect(dialog.getByText('Plattform wählen')).toBeVisible()
    await expect(dialog.getByText('Bitte vollständige URL mit https:// angeben')).toBeVisible()
    expect(api.callsTo('PUT', `/api/clubs/${ids.club}`)).toHaveLength(0)

    await selectOption(page, dialog.locator('.social-link-row').last(), 'Plattform', 'Mastodon')
    await dialog.getByLabel('Profil-URL').last().fill('https://mastodon.example/@tsv')
    await dialog.getByRole('button', { name: 'Speichern' }).click()
    await expect(snackbar(page)).toContainText('Verein wurde gespeichert.')
    expect(api.lastBody('PUT', `/api/clubs/${ids.club}`)).toMatchObject({
      socialMediaLinks: [{ platform: 1, url: 'https://www.instagram.com/tsv' }, { platform: 5, url: 'https://mastodon.example/@tsv' }]
    })
  })

  test('Social-Media-Links erscheinen als Icons in der Übersicht', async ({ page, api }) => {
    api.get(`/api/clubs/${ids.club}`, data.club({
      socialMediaLinks: [{ platform: 1, url: 'https://www.instagram.com/tsv' }, { platform: 0, url: 'https://facebook.com/tsv' }, { platform: 2, url: 'javascript:alert(1)' }]
    }))
    await page.goto(`/clubs/${ids.club}`)
    await expect(page.getByRole('link', { name: 'Instagram: https://www.instagram.com/tsv' })).toHaveAttribute('href', 'https://www.instagram.com/tsv')
    await expect(page.getByRole('link', { name: 'Facebook: https://facebook.com/tsv' })).toBeVisible()
    await expect(page.getByRole('link', { name: /YouTube/ })).toHaveCount(0)
  })

  test('Abteilung anlegen', async ({ page, api }) => {
    api
      .post('/api/departments', ({ body }) => data.department({ ...(body as object), id: 'd-new' }))
      .get('/api/departments/d-new', data.department({ id: 'd-new', name: 'Tischtennis' }))
    await page.goto(`/clubs/${ids.club}`)
    await page.getByRole('button', { name: 'Abteilung anlegen' }).click()
    await page.getByRole('dialog').getByLabel('Name *').fill('Tischtennis')
    await page.getByRole('dialog').getByRole('button', { name: 'Anlegen' }).click()
    await expect(page).toHaveURL(/\/departments\/d-new$/)
    expect(api.lastBody('POST', '/api/departments')).toMatchObject({ name: 'Tischtennis', clubId: ids.club, parentDepartmentId: null, website: null })
  })

  test('Verein löschen nach Bestätigung', async ({ page, api }) => {
    api.delete(`/api/clubs/${ids.club}`, null).get('/api/clubs/search', data.paged([]))
    await page.goto(`/clubs/${ids.club}`)
    await page.getByRole('button', { name: 'Verein löschen' }).click()
    await expect(page.getByRole('dialog')).toContainText('„TSV Musterstadt“ wird gelöscht')
    await page.getByRole('dialog').getByRole('button', { name: 'Löschen' }).click()
    await expect(page).toHaveURL(/\/clubs$/)
    expect(api.callsTo('DELETE', `/api/clubs/${ids.club}`)).toHaveLength(1)
  })

  test('verlinkt die öffentliche Seite in neuem Tab, Bearbeiten nur in der Übersicht', async ({ page, api }) => {
    api.get('/api/auth/status', { isAuthenticated: true, userId: data.user().id, hasPasskey: true, passkeyRequired: false, publicWebUrl: 'https://portal.test/' })
    await page.goto(`/clubs/${ids.club}`)
    const link = page.getByRole('link', { name: 'Öffentliche Seite' })
    await expect(link).toHaveAttribute('href', `https://portal.test/vereine/${ids.club}/tsv-musterstadt`)
    await expect(link).toHaveAttribute('target', '_blank')
    await expect(page.getByRole('main').getByRole('button', { name: 'Bearbeiten' }).first()).toBeVisible()
  })

  test('ohne Portal-URL kein Link zur öffentlichen Seite', async ({ page }) => {
    await page.goto(`/clubs/${ids.club}`)
    await expect(page.getByRole('heading', { name: 'TSV Musterstadt', level: 1 })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Öffentliche Seite' })).toHaveCount(0)
  })

  test('ohne Rechte keine Bearbeiten- und Lösch-Aktionen', async ({ page, api }) => {
    api.get(`/api/clubs/${ids.club}`, data.club({ canEdit: false, canDelete: false, canManageContacts: false }))
    await page.goto(`/clubs/${ids.club}`)
    await expect(page.getByRole('heading', { name: 'TSV Musterstadt' })).toBeVisible()
    await expect(page.getByRole('button', { name: /Bearbeiten|Verein löschen|Bild hochladen|anlegen/ })).toHaveCount(0)
  })

  test('Fehler beim Laden wird angezeigt', async ({ page, api }) => {
    api.get(`/api/clubs/${ids.club}`, fail(500, 'kaputt'))
    await page.goto(`/clubs/${ids.club}`)
    await expect(page.getByText('Der Verein konnte nicht geladen werden.')).toBeVisible()
  })
})

test.describe('Abteilungsdetails', () => {
  test('Breadcrumb und Hierarchiezeile führen zurück zum Verein', async ({ page, api, signIn }) => {
    await signIn()
    api.get(`/api/departments/${ids.department}`, data.department()).get(`/api/clubs/${ids.club}`, data.club())
    await page.goto(`/departments/${ids.department}`)
    await expect(page.getByRole('heading', { name: 'Fußball', level: 1 })).toBeVisible()
    await expect(page.getByTestId('organization-hierarchy')).toHaveText('Abteilung von TSV Musterstadt')
    await page.locator('.v-breadcrumbs').getByRole('link', { name: 'TSV Musterstadt' }).click()
    await expect(page).toHaveURL(new RegExp(`/clubs/${ids.club}$`))
  })

  test('listet Gruppen und legt eine neue Gruppe an', async ({ page, api, signIn }) => {
    await signIn()
    api
      .get(`/api/departments/${ids.department}`, data.department({
        groups: [data.departmentSummary({ id: ids.group, name: 'Bambini', parentDepartmentId: ids.department, description: 'Training: freitags 16 Uhr\nSporthalle Nord', contactPersonsCount: 2 })]
      }))
      .post('/api/departments', ({ body }) => data.department({ ...(body as object), id: 'g-new', isGroup: true }))
      .get('/api/departments/g-new', data.department({ id: 'g-new', name: 'Alte Herren', isGroup: true, parentDepartmentId: ids.department, parentDepartmentName: 'Fußball' }))
    await page.goto(`/departments/${ids.department}`)

    const groups = page.getByRole('link', { name: /Bambini/ })
    await expect(groups).toHaveAttribute('href', `/departments/${ids.group}`)
    await expect(groups).toContainText('Training: freitags 16 Uhr')
    await expect(groups).toContainText('2 Kontaktpersonen')

    await page.getByRole('button', { name: 'Gruppe anlegen' }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toContainText('Gruppe anlegen')
    await expect(dialog.getByLabel('Straße und Hausnummer')).toHaveCount(0)
    await dialog.getByLabel('Name *').fill('Alte Herren')
    await dialog.getByLabel('Infos').fill('Mittwochs 20 Uhr')
    await dialog.getByRole('button', { name: 'Anlegen' }).click()

    await expect(page).toHaveURL(/\/departments\/g-new$/)
    expect(api.lastBody('POST', '/api/departments')).toMatchObject({
      name: 'Alte Herren', description: 'Mittwochs 20 Uhr', clubId: ids.club, parentDepartmentId: ids.department, street: null
    })
  })
})

test.describe('Gruppendetails', () => {
  const group = (overrides = {}) => data.department({
    id: ids.group, name: 'Bambini', isGroup: true, parentDepartmentId: ids.department, parentDepartmentName: 'Fußball',
    description: 'Für Kinder von 4 bis 6 Jahren.\nInfos unter https://tsv.test/bambini', canDelete: false, ...overrides
  })

  test('zeigt die Hierarchie und den Infotext', async ({ page, api, signIn }) => {
    await signIn()
    api.get(`/api/departments/${ids.group}`, group())
    await page.goto(`/departments/${ids.group}`)

    await expect(page.getByRole('heading', { name: 'Bambini', level: 1 })).toBeVisible()
    const hierarchy = page.getByTestId('organization-hierarchy')
    await expect(hierarchy).toHaveText('Gruppe der Abteilung Fußball · TSV Musterstadt')
    await expect(hierarchy.getByRole('link', { name: 'Fußball' })).toHaveAttribute('href', `/departments/${ids.department}`)
    await expect(hierarchy.getByRole('link', { name: 'TSV Musterstadt' })).toHaveAttribute('href', `/clubs/${ids.club}`)
    await expect(page.locator('.v-breadcrumbs').getByRole('link', { name: 'Fußball' })).toBeVisible()

    await expect(page.getByText('Für Kinder von 4 bis 6 Jahren.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'https://tsv.test/bambini' })).toHaveAttribute('href', 'https://tsv.test/bambini')
    // Groups have no own groups and no delete right for their editors
    await expect(page.getByRole('heading', { name: 'Gruppen' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Gruppe löschen' })).toHaveCount(0)
  })

  test('Kontaktperson einladen ordnet sie der Gruppe zu', async ({ page, api, signIn }) => {
    await signIn()
    api
      .get(`/api/departments/${ids.group}`, group())
      .get('/api/users/contacts', [])
      .post('/api/invitations/invite', { success: true, message: 'OK', isExistingUser: false })
    await page.goto(`/departments/${ids.group}?tab=contacts`)
    await page.getByRole('button', { name: /Kontaktperson hinzufügen/ }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('tab', { name: 'Neu einladen' }).click()
    await dialog.getByLabel('E-Mail-Adresse *').fill('trainer@example.test')
    await expect(dialog.getByText('Darf die Gruppe bearbeiten')).toBeVisible()
    await dialog.getByRole('button', { name: 'Einladen und zuordnen' }).click()

    expect(api.lastBody('POST', '/api/invitations/invite')).toMatchObject({ email: 'trainer@example.test', clubId: null, departmentId: ids.group })
  })
})
