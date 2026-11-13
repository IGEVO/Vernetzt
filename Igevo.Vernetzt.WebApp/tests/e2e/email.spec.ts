import { data, expect, test } from './support/fixtures'

const { ids } = data

/** Four candidates: two sports-club chairs (one in a department), a music-club chair and a sports-club treasurer. */
const candidates = [
  data.recipient({ assignmentId: 'as-max', userId: 'u-max', firstName: 'Max', lastName: 'Beispiel', positionId: 'pos-chair', positionName: 'Vorsitz' }),
  data.recipient({ assignmentId: 'as-tom', userId: 'u-tom', firstName: 'Tom', lastName: 'Tor', organizationId: ids.department, organizationName: 'Fußball', organizationType: 1, parentClubId: ids.club, parentClubName: 'TSV Musterstadt', positionId: 'pos-chair', positionName: 'Vorsitz' }),
  data.recipient({ assignmentId: 'as-lea', userId: 'u-lea', firstName: 'Lea', lastName: 'Lang', organizationId: ids.club2, organizationName: 'Musikverein Harmonie', clubTypeId: 'ct-music', positionId: 'pos-chair', positionName: 'Vorsitz', emailBadgeLabel: 'Vereinsadresse' }),
  data.recipient({ assignmentId: 'as-kai', userId: 'u-kai', firstName: 'Kai', lastName: 'Kasse', positionId: 'pos-treasurer', positionName: 'Kasse' })
]

test.describe('E-Mail versenden', () => {
  test.beforeEach(async ({ api, signIn }) => {
    await signIn()
    api.get('/api/users/recipient-candidates', candidates)
  })

  test('Schnellauswahl kombiniert Gruppen mit UND, innerhalb einer Gruppe mit ODER', async ({ page }) => {
    await page.goto('/emails/send')
    await expect(page.getByText('Sportverein (3)')).toBeVisible()
    await expect(page.getByText('Vorsitz (3)')).toBeVisible()

    await page.getByText('Sportverein (3)').click()
    await expect(page.getByText('3 Kontakte werden vorausgewählt')).toBeVisible()
    await page.getByText('Vorsitz (3)').click()
    await expect(page.getByText('2 Kontakte werden vorausgewählt')).toBeVisible()
    await page.getByText('Kasse (1)').click()
    await expect(page.getByText('3 Kontakte werden vorausgewählt')).toBeVisible()

    await page.getByRole('button', { name: 'Filter zurücksetzen' }).click()
    await expect(page.getByRole('button', { name: 'Ohne Filter weiter' })).toBeVisible()
  })

  test('Vereinsfilter schließt Abteilungen und Gruppen des Vereins ein', async ({ page }) => {
    await page.goto('/emails/send')
    await page.locator('.v-field').filter({ has: page.getByRole('combobox', { name: 'Vereine auswählen' }) }).click()
    await page.getByRole('option', { name: /TSV Musterstadt/ }).click()
    await page.keyboard.press('Escape')
    await expect(page.getByText('3 Kontakte werden vorausgewählt')).toBeVisible()
  })

  test('kompletter Versand: Vorauswahl anpassen, verfassen, bestätigen', async ({ page, api }) => {
    api.post('/api/email/bulk', { success: true, recipientCount: 2 })
    await page.goto('/emails/send')
    await page.getByText('Vorsitz (3)').click()
    await page.getByText('Sportverein (3)').click()
    await page.getByRole('button', { name: 'Weiter', exact: true }).click()

    // Step 2: only the preselection is listed, a search covers all contacts.
    await expect(page.getByText('2 von 4 ausgewählt')).toBeVisible()
    await expect(page.getByRole('listitem').filter({ hasText: 'Lea Lang' })).toHaveCount(0)
    await page.getByRole('textbox', { name: 'Kontakte durchsuchen' }).fill('kai')
    await page.getByRole('listitem').filter({ hasText: 'Kai Kasse' }).click()
    await page.getByRole('textbox', { name: 'Kontakte durchsuchen' }).fill('')
    await page.getByRole('listitem').filter({ hasText: 'Tom Tor' }).click()
    await expect(page.getByText('2 von 4 ausgewählt')).toBeVisible()
    await page.getByRole('button', { name: 'Weiter (2)' }).click()

    // Step 3 + 4
    await page.getByRole('button', { name: 'Weiter', exact: true }).click()
    const send = page.getByRole('button', { name: 'E-Mail senden (2)' })
    await expect(send).toBeDisabled()
    await page.getByLabel('Betreff *').fill('Einladung zur Versammlung')
    await page.locator('.tiptap').fill('Hallo zusammen!')
    await send.click()
    await page.getByRole('dialog').getByRole('button', { name: 'Senden' }).click()

    await expect(page).toHaveURL(/\/$/)
    const body = api.lastBody<{ subject: string; htmlBody: string; plainTextBody: string; recipientAssignmentIds: string[]; recipientUserIds: string[] }>('POST', '/api/email/bulk')
    expect(body.subject).toBe('Einladung zur Versammlung')
    expect(body.plainTextBody).toContain('Hallo zusammen!')
    expect(body.recipientAssignmentIds.sort()).toEqual(['as-kai', 'as-max'])
    expect(body.recipientUserIds).toEqual([])
  })

  test('ohne Filter direkt alle Kontakte auswählen', async ({ page }) => {
    await page.goto('/emails/send')
    await page.getByRole('button', { name: 'Ohne Filter weiter' }).click()
    await expect(page.getByText('0 von 4 ausgewählt')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Weiter (0)' })).toBeDisabled()
    await page.getByRole('button', { name: 'Angezeigte auswählen' }).click()
    await expect(page.getByText('4 von 4 ausgewählt')).toBeVisible()
    await expect(page.getByText('Vereinsadresse')).toBeVisible()
  })

  test('Versand über eigenen E-Mail-Client (Relay)', async ({ page, api }) => {
    api.post('/api/email/relay/token', { tokenId: 't', tokenAddress: 'abc123@relay.example', expiresAt: '2026-11-06T10:00:00Z', recipientCount: 4 })
    await page.goto('/emails/send')
    await page.getByRole('button', { name: 'Ohne Filter weiter' }).click()
    await page.getByRole('button', { name: 'Angezeigte auswählen' }).click()
    await page.getByRole('button', { name: 'Weiter (4)' }).click()
    await page.getByText('Eigener E-Mail-Client').click()
    await page.getByRole('button', { name: 'Weiter', exact: true }).click()
    await page.getByRole('button', { name: 'E-Mail-Adresse erzeugen' }).click()
    await expect(page.getByText('abc123@relay.example')).toBeVisible()
    expect(api.lastBody<{ recipientAssignmentIds: string[] }>('POST', '/api/email/relay/token').recipientAssignmentIds).toHaveLength(4)
  })

  test('Relay-Option ist deaktiviert, wenn der Dienst fehlt', async ({ page, api }) => {
    api.get('/api/email/relay/status', false)
    await page.goto('/emails/send')
    await page.getByRole('button', { name: 'Ohne Filter weiter' }).click()
    await page.getByRole('button', { name: 'Angezeigte auswählen' }).click()
    await page.getByRole('button', { name: 'Weiter (4)' }).click()
    await expect(page.getByText('Der E-Mail-Relay-Dienst ist nicht konfiguriert.')).toBeVisible()
  })
})

test.describe('E-Mail versenden: Standortfilter', () => {
  /**
   * TSV (club) uses "Sporthalle Nord" and has one flagged contact (Max) plus Kai (Kasse).
   * The football department uses the hall too, nobody is flagged there (Tom, Tim).
   * The youth department of the TSV has no own location: the club's location is not inherited (Dora).
   */
  const locationCandidates = [
    data.recipient({ assignmentId: 'as-max', userId: 'u-max', firstName: 'Max', lastName: 'Beispiel', locationIds: ['loc-hall'], receivesLocationInfo: true }),
    data.recipient({ assignmentId: 'as-kai', userId: 'u-kai', firstName: 'Kai', lastName: 'Kasse', positionId: 'pos-treasurer', positionName: 'Kasse', locationIds: ['loc-hall'] }),
    data.recipient({ assignmentId: 'as-tom', userId: 'u-tom', firstName: 'Tom', lastName: 'Tor', organizationId: ids.department, organizationName: 'Fußball', organizationType: 1, parentClubId: ids.club, parentClubName: 'TSV Musterstadt', locationIds: ['loc-hall'] }),
    data.recipient({ assignmentId: 'as-tim', userId: 'u-tim', firstName: 'Tim', lastName: 'Trainer', organizationId: ids.department, organizationName: 'Fußball', organizationType: 1, parentClubId: ids.club, parentClubName: 'TSV Musterstadt', positionId: 'pos-treasurer', positionName: 'Kasse', locationIds: ['loc-hall'] }),
    data.recipient({ assignmentId: 'as-dora', userId: 'u-dora', firstName: 'Dora', lastName: 'Jugend', organizationId: 'dep-youth', organizationName: 'Jugend', organizationType: 1, parentClubId: ids.club, parentClubName: 'TSV Musterstadt', locationIds: [] }),
    data.recipient({ assignmentId: 'as-lea', userId: 'u-lea', firstName: 'Lea', lastName: 'Lang', organizationId: ids.club2, organizationName: 'Musikverein Harmonie', clubTypeId: 'ct-music', locationIds: ['loc-pool'] })
  ]

  test.beforeEach(async ({ api, signIn }) => {
    await signIn()
    api.get('/api/users/recipient-candidates', locationCandidates)
  })

  test('wählt markierte Personen, sonst alle Kontaktpersonen der Organisation, ohne Vererbung', async ({ page }) => {
    await page.goto('/emails/send')
    await page.getByText('Sporthalle Nord (2 Org.)').click()
    // Max (flagged in the club), Tom and Tim (nobody flagged in the department); not Kai, Dora or Lea
    await expect(page.getByText('3 Kontakte werden vorausgewählt')).toBeVisible()

    await page.getByRole('button', { name: 'Weiter' }).click()
    await expect(page.getByTestId('location-rule-hint')).toContainText('Erhält Standort-Infos')
    await expect(page.getByText('3 von 6 ausgewählt')).toBeVisible()
    await expect(page.getByText('Max Beispiel')).toBeVisible()
    await expect(page.getByText('Tom Tor')).toBeVisible()
    await expect(page.getByText('Tim Trainer')).toBeVisible()
    await expect(page.getByText('Kai Kasse')).toHaveCount(0)
    await expect(page.getByText('Dora Jugend')).toHaveCount(0)
  })

  test('Positionsfilter ersetzt die Markierung', async ({ page }) => {
    await page.goto('/emails/send')
    await page.getByText('Sporthalle Nord (2 Org.)').click()
    await page.getByText(/^Kasse \(\d+\)$/).click()
    // Kai and Tim hold the position at organizations using the hall; Max's flag no longer matters
    await expect(page.getByText('2 Kontakte werden vorausgewählt')).toBeVisible()

    await page.getByRole('button', { name: 'Weiter' }).click()
    await expect(page.getByTestId('location-rule-hint')).toContainText('nicht berücksichtigt')
    await expect(page.getByText('Kai Kasse')).toBeVisible()
    await expect(page.getByText('Tim Trainer')).toBeVisible()
  })
})
