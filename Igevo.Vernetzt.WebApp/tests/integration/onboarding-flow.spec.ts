import { expect, test, type Page } from '@playwright/test'
import { linkTo, waitForMail } from './support/mailpit'
import { addVirtualAuthenticator, switchToNewVirtualAuthenticator } from './support/passkeys'

/**
 * Real end-to-end flow against a fresh backend (see playwright.integration.config.ts):
 * admin signs in (login link), has to set up a passkey and sign in with it (SystemAdmin passkey policy) →
 * adds a second passkey → configures mandatory settings → creates a club →
 * invites a contact person → the invitee registers and signs in via e-mail → assignment is visible everywhere.
 */
test.describe.configure({ mode: 'serial' })

const adminEmail = 'admin@test.local'
const run = Date.now().toString(36)
const inviteeEmail = `kontakt.${run}@example.test`
const clubName = `TSV Integration ${run}`

let admin: Page
let clubUrl = ''

test.beforeAll(async ({ browser }) => {
  admin = await (await browser.newContext()).newPage()
  await addVirtualAuthenticator(admin)
})
test.afterAll(async () => { await admin?.context().close() })

test('Admin meldet sich per Anmeldelink an, richtet einen Passkey ein und meldet sich damit an', async () => {
  const since = new Date()
  await admin.goto('/login?returnUrl=%2Fadmin%2Fusers')
  await admin.getByLabel('E-Mail-Adresse').fill(adminEmail)
  await admin.getByLabel('E-Mail-Adresse').press('Enter')
  await expect(admin.getByText(`Wir haben dir einen Anmeldelink an ${adminEmail} gesendet`)).toBeVisible()

  const mail = await waitForMail(adminEmail, { since, subject: /Anmeldelink/ })
  await admin.goto(linkTo(mail, '/login'))

  // A login link is not enough for a SystemAdmin: the session is restricted until signed in with a passkey.
  await expect(admin.getByRole('heading', { name: 'Anmeldung mit Passkey', level: 1 })).toBeVisible()
  await admin.getByRole('button', { name: 'Passkey einrichten' }).click()
  await expect(admin.getByText('Dein Passkey ist eingerichtet.')).toBeVisible()
  await admin.getByRole('button', { name: 'Mit Passkey anmelden' }).click()
  await expect(admin.getByRole('heading', { name: 'Benutzerverwaltung', level: 1 })).toBeVisible()
})

test('Admin richtet unter „Mein Konto“ einen weiteren Passkey ein', async () => {
  await admin.goto('/konto?tab=sicherheit')
  const passkeys = admin.getByRole('main').getByRole('listitem')
  await expect(passkeys).toHaveCount(1)
  // Second device: the first authenticator already holds a passkey for this account.
  await switchToNewVirtualAuthenticator(admin)
  await admin.getByLabel('Name für den neuen Passkey (optional)').fill('Testgerät')
  await admin.getByRole('button', { name: 'Passkey einrichten' }).click()
  await expect(admin.getByText('Passkey wurde eingerichtet.')).toBeVisible()
  await expect(passkeys).toHaveCount(2)
  await expect(passkeys.filter({ hasText: 'Testgerät' })).toBeVisible()
})

test('Admin hinterlegt die Pflichtangaben für Einladungen', async () => {
  await admin.goto('/admin/settings')
  await expect(admin.getByText('Solange nicht alle drei Felder ausgefüllt sind')).toBeVisible()
  await admin.getByLabel('Name *').fill('Vernetzt Testbetrieb')
  await admin.getByLabel('E-Mail-Adresse *').fill(adminEmail)
  await admin.getByLabel('URL der Datenschutzerklärung *').fill('https://example.test/datenschutz')
  await admin.getByRole('button', { name: 'Speichern' }).click()
  await expect(admin.getByText('Einstellungen wurden gespeichert.')).toBeVisible()
  await admin.reload()
  await expect(admin.getByLabel('URL der Datenschutzerklärung *')).toHaveValue('https://example.test/datenschutz')
})

test('Admin legt einen Verein an', async () => {
  await admin.goto('/clubs')
  await admin.getByRole('button', { name: 'Verein anlegen' }).click()
  const dialog = admin.getByRole('dialog')
  await dialog.getByLabel('Name *').fill(clubName)
  await dialog.getByLabel('Ort').fill('Teststadt')
  await dialog.getByRole('button', { name: 'Anlegen' }).click()
  await expect(admin.getByRole('heading', { name: clubName, level: 1 })).toBeVisible()
  clubUrl = new URL(admin.url()).pathname

  await admin.goto(`/clubs?q=${encodeURIComponent(run)}`)
  await expect(admin.getByText(clubName)).toBeVisible()
})

test('Admin lädt eine neue Kontaktperson mit Zuordnungsdetails ein', async () => {
  await admin.goto(`${clubUrl}?tab=contacts`)
  await admin.getByRole('button', { name: 'Kontaktperson hinzufügen' }).click()
  const dialog = admin.getByRole('dialog')
  await dialog.getByRole('tab', { name: 'Neu einladen' }).click()
  await dialog.getByLabel('Persönliche E-Mail-Adresse *').fill(inviteeEmail)
  await dialog.getByLabel('Zuordnungsdetails (optional)').fill('1. Mannschaft')
  await dialog.getByLabel('Darf den Verein bearbeiten').check()
  await dialog.getByRole('button', { name: 'Einladen und zuordnen' }).click()
  await expect(admin.getByText(`Einladung an ${inviteeEmail} wurde versendet.`)).toBeVisible()
})

test('Eingeladene Person registriert sich und meldet sich per E-Mail an', async ({ browser }) => {
  const since = new Date(Date.now() - 60_000)
  const invitee = await (await browser.newContext()).newPage()
  try {
    const invitation = await waitForMail(inviteeEmail, { since, subject: /Einladung/i })
    await invitee.goto(linkTo(invitation, '/onboarding'))

    await expect(invitee.getByText(`Einladung für ${inviteeEmail}`)).toBeVisible()
    await expect(invitee.getByText(clubName)).toBeVisible()
    await invitee.getByLabel('Vorname').fill('Nora')
    await invitee.getByLabel('Nachname').fill('Neu')
    await invitee.getByRole('radio', { name: /Mit Anmeldelink per E-Mail/ }).check()
    await invitee.getByRole('checkbox', { name: /Ich akzeptiere/ }).check()
    const registered = new Date()
    await invitee.getByRole('button', { name: 'Registrierung abschließen' }).click()
    await expect(invitee.getByText('Fast geschafft!')).toBeVisible()

    const login = await waitForMail(inviteeEmail, { since: registered, subject: /Anmeldelink/ })
    await invitee.goto(linkTo(login, '/login'))
    await expect(invitee.getByRole('heading', { level: 1 })).toContainText(/Willkommen, Nora|Hallo Nora/)

    // The pre-linked assignment exists with its details and edit right.
    await invitee.goto('/konto?tab=zuordnungen')
    await expect(invitee.getByText(clubName)).toBeVisible()
    await invitee.goto(`${clubUrl}?tab=contacts`)
    const entry = invitee.getByRole('listitem').filter({ hasText: 'Nora Neu' })
    await expect(entry).toContainText('1. Mannschaft')
    await expect(entry).toContainText('Darf bearbeiten')
  } finally {
    await invitee.context().close()
  }
})

test('Admin sieht die neue Person mit Passkey-Status und im Audit-Log', async () => {
  await admin.goto('/admin/users')
  await expect(admin.getByRole('row').filter({ hasText: 'Nora Neu' }).getByTitle('Kein Passkey eingerichtet')).toBeVisible()
  await expect(admin.getByRole('row').filter({ hasText: adminEmail }).getByTitle('2 Passkey(s) eingerichtet')).toBeVisible()

  await admin.goto('/admin/audit-log')
  await expect(admin.getByRole('row').filter({ hasText: clubName }).first()).toBeVisible()
})
