import { data, expect, raw, selectOption, snackbar, test } from './support/fixtures'

const { ids } = data

test.describe('Kontaktübersicht', () => {
  test('zeigt Avatar-Initialen, Vereine und Abteilungen mit Links', async ({ page, api, signIn }) => {
    await signIn()
    api.get('/api/users/contacts/search', ({ query }) => data.paged(query.get('searchTerm') === 'lea' ? [] : [
      data.contact({
        organizations: [
          { organizationId: ids.club, organizationName: 'TSV Musterstadt', type: 0, positionName: 'Vorsitz' },
          { organizationId: ids.department, organizationName: 'Fußball', type: 1, parentName: 'TSV Musterstadt' }
        ]
      })
    ]))

    await page.goto('/kontakte')
    const card = page.locator('.v-card').filter({ hasText: 'Max Beispiel' })
    await expect(card.getByText('MB')).toBeVisible()
    await expect(card.getByText('Vorsitz')).toBeVisible()
    await expect(card.getByRole('link', { name: 'TSV Musterstadt', exact: true })).toHaveAttribute('href', `/clubs/${ids.club}`)
    await expect(card.getByRole('link', { name: 'TSV Musterstadt › Fußball' })).toHaveAttribute('href', `/departments/${ids.department}`)
    await expect(card.getByRole('link', { name: 'max@example.test' })).toHaveAttribute('href', 'mailto:max@example.test')

    await page.getByRole('textbox', { name: 'Kontakte suchen' }).fill('lea')
    await expect(page.getByText('Keine Kontakte gefunden')).toBeVisible()
    expect(api.callsTo('GET', '/api/users/contacts/search').at(-1)?.query.get('searchTerm')).toBe('lea')
  })
})

test.describe('Kontaktpersonen eines Vereins', () => {
  test.beforeEach(async ({ api, signIn }) => {
    await signIn()
    api
      .get(`/api/clubs/${ids.club}`, data.club())
      .get('/api/users/contacts', [data.contact(), data.contact({ id: 'u-lea', firstName: 'Lea', lastName: 'Lang', email: 'lea@example.test', organizations: [] })])
  })

  test('Liste mit Kontaktdaten', async ({ page }) => {
    await page.goto(`/clubs/${ids.club}?tab=contacts`)
    await expect(page.getByText('1 Kontaktperson')).toBeVisible()
    await expect(page.getByText('Max Beispiel')).toBeVisible()
    await expect(page.getByRole('link', { name: '0151 123' })).toHaveAttribute('href', 'tel:0151 123')
  })

  test('bestehende Person zuordnen', async ({ page, api }) => {
    api.post(`/api/clubs/${ids.club}/contact-persons/:userId`, null)
    await page.goto(`/clubs/${ids.club}?tab=contacts`)
    await page.getByRole('button', { name: 'Kontaktperson hinzufügen' }).click()
    const dialog = page.getByRole('dialog')

    await dialog.getByRole('combobox', { name: 'Person suchen *' }).click()
    await expect(page.getByRole('option', { name: /Max Beispiel/ })).toHaveClass(/v-list-item--disabled/)
    await page.getByRole('option', { name: /Lea Lang/ }).click()
    await selectOption(page, dialog, 'Position (optional)', 'Kasse')
    await dialog.getByLabel('Zuordnungsdetails (optional)').fill('Jugend')
    await dialog.getByLabel('Darf den Verein bearbeiten').check()
    await dialog.getByRole('button', { name: 'Zuordnen' }).click()

    await expect(snackbar(page)).toContainText('Kontaktzuordnung wurde gespeichert.')
    const call = api.callsTo('POST', `/api/clubs/${ids.club}/contact-persons/u-lea`)[0]!
    expect(call.query.get('positionId')).toBe('pos-treasurer')
    expect(call.query.get('canEdit')).toBe('true')
    expect(call.body).toEqual({ notes: 'Jugend' })
  })

  test('neue Person einladen und zuordnen', async ({ page, api }) => {
    api.post('/api/invitations/invite', { success: true, message: 'OK', isExistingUser: false })
    await page.goto(`/clubs/${ids.club}?tab=contacts`)
    await page.getByRole('button', { name: 'Kontaktperson hinzufügen' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('tab', { name: 'Neu einladen' }).click()

    const submit = dialog.getByRole('button', { name: 'Einladen und zuordnen' })
    await expect(submit).toBeDisabled()
    await dialog.getByLabel('Persönliche E-Mail-Adresse *').fill('neu@example.test')
    await dialog.getByLabel('Zuordnungsdetails (optional)').fill('1. Mannschaft')
    await submit.click()

    await expect(snackbar(page)).toContainText('Einladung an neu@example.test wurde versendet.')
    expect(api.lastBody('POST', '/api/invitations/invite')).toEqual({ email: 'neu@example.test', clubId: ids.club, departmentId: null, contactPositionId: null, canEdit: false, assignmentDetails: '1. Mannschaft', contextEmail: null })
  })

  test('Amtsadresse beim Einladen erkennen und als Amtsadresse übernehmen', async ({ page, api }) => {
    api.post('/api/invitations/invite', { success: true, message: 'OK', isExistingUser: false })
    await page.goto(`/clubs/${ids.club}?tab=contacts`)
    await page.getByRole('button', { name: 'Kontaktperson hinzufügen' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('tab', { name: 'Neu einladen' }).click()

    await dialog.getByLabel('Persönliche E-Mail-Adresse *').fill('vorstand@tsv.test')
    await expect(dialog.getByText('sieht nach einer Amtsadresse aus')).toBeVisible()
    await dialog.getByRole('button', { name: 'Als Amtsadresse übernehmen' }).click()
    await expect(dialog.getByLabel('Amtsadresse für diese Zuordnung (optional)')).toHaveValue('vorstand@tsv.test')
    await expect(dialog.getByLabel('Persönliche E-Mail-Adresse *')).toHaveValue('')

    const submit = dialog.getByRole('button', { name: 'Einladen und zuordnen' })
    await expect(submit).toBeDisabled()
    await dialog.getByLabel('Persönliche E-Mail-Adresse *').fill('vorstand@tsv.test')
    await expect(dialog.getByText('Die Amtsadresse muss sich von der persönlichen Adresse unterscheiden')).toBeVisible()
    await expect(submit).toBeDisabled()
    await dialog.getByLabel('Persönliche E-Mail-Adresse *').fill('max.muster@example.test')
    await submit.click()

    await expect(snackbar(page)).toContainText('Einladung an max.muster@example.test wurde versendet.')
    expect(api.lastBody('POST', '/api/invitations/invite')).toMatchObject({ email: 'max.muster@example.test', clubId: ids.club, contextEmail: 'vorstand@tsv.test' })
  })

  test('abgelehnte Einladung bleibt im Dialog sichtbar', async ({ page, api }) => {
    api.post('/api/invitations/invite', { success: false, message: 'Ein Benutzer mit dieser E-Mail-Adresse existiert bereits.', isExistingUser: true })
    await page.goto(`/clubs/${ids.club}?tab=contacts`)
    await page.getByRole('button', { name: 'Kontaktperson hinzufügen' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('tab', { name: 'Neu einladen' }).click()
    await dialog.getByLabel('Persönliche E-Mail-Adresse *').fill('max@example.test')
    await dialog.getByLabel('Persönliche E-Mail-Adresse *').press('Enter')
    await expect(dialog.getByText('Ein Benutzer mit dieser E-Mail-Adresse existiert bereits.')).toBeVisible()
  })

  test('Zuordnung bearbeiten und entfernen', async ({ page, api }) => {
    api.put(`/api/clubs/${ids.club}/contact-persons/u-max`, null).delete(`/api/clubs/${ids.club}/contact-persons/u-max`, raw(204))
    await page.goto(`/clubs/${ids.club}?tab=contacts`)

    await page.getByRole('button', { name: 'Zuordnung bearbeiten' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Zuordnungsdetails (optional)').fill('Sponsoring')
    await dialog.getByRole('button', { name: 'Speichern' }).click()
    expect(api.lastBody('PUT', `/api/clubs/${ids.club}/contact-persons/u-max`)).toEqual({ notes: 'Sponsoring' })

    await page.getByRole('button', { name: 'Zuordnung entfernen' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Löschen' }).click()
    await expect(snackbar(page)).toContainText('Kontaktzuordnung wurde entfernt.')
  })
})
