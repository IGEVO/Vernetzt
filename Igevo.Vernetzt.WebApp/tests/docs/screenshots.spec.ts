import { test as base, expect, type Locator, type Page } from '@playwright/test'
import { MockApi } from '../e2e/support/mockApi'
import * as data from '../e2e/support/data'
import type { ContactAssignmentDto, UserDto } from '../../src/types/api'

/**
 * Screenshots for the user documentation, rendered against the mocked API with sample data.
 * Each test produces the images of one documentation page; file names match the Markdown references.
 * Unlike the UI tests, unmocked API calls do not fail a test – they are logged so missing data shows up.
 * Images are compared with the committed ones and only rewritten on a visible change (see playwright.docs.config.ts).
 */
const { ids } = data
const NOW = new Date('2026-11-20T10:00:00+01:00')

const erika = data.user({ email: 'erika.muster@web.de', phoneNumber: '0151 2345678' })
const ada = data.admin({ email: 'ada.admin@gemeinde.test' })

const test = base.extend<{ api: MockApi; signIn: (user?: UserDto, options?: { hasPasskey?: boolean }) => Promise<void> }>({
  api: async ({ page }, use, testInfo) => {
    await page.clock.setFixedTime(NOW)
    await page.addInitScript(() => {
      if ('PublicKeyCredential' in window) Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => false })
    })
    const api = new MockApi()
    api
      .get('/api/auth/status', { isAuthenticated: false })
      .get('/api/clubtypes/active', data.clubTypes)
      .get('/api/contactpositions/active', positions)
      .get('/api/locations/active', data.locations)
      .get('/api/email/relay/status', true)
      .get('/api/support/contact', { name: 'Gemeindeverwaltung', email: 'vernetzt@gemeinde.test' })
    await api.install(page)
    await use(api)
    if (api.unhandled.length) console.warn(`[${testInfo.title}] nicht gemockt: ${[...new Set(api.unhandled)].join(', ')}`)
  },
  signIn: async ({ api }, use) => {
    await use(async (user = erika, options = {}) => {
      api
        .get('/api/auth/status', { isAuthenticated: true, userId: user.id, hasPasskey: options.hasPasskey ?? true, passkeyRequired: false })
        .get('/api/users/me', user)
        .get('/api/auth/passkeys', options.hasPasskey === false ? [] : [{ credentialId: 'cred-1', name: 'Laptop', createdAt: '2026-10-01T08:00:00Z' }])
        .get('/api/holidays', holidays)
        .get('/api/stats/dashboard', dashboard)
        .get('/api/users/me/contact-assignments', [data.myAssignment({ contextEmail: 'vorstand@tsv-musterstadt.de', contextEmailVerified: true })])
    })
  }
})

/** Saves a screenshot of the page (or of one element) once data, fonts and transitions have settled. */
async function shot(page: Page, name: string, target?: Locator | { fullPage: true }): Promise<void> {
  await page.waitForLoadState('networkidle')
  await page.evaluate(() => document.fonts.ready)
  await page.waitForTimeout(600)
  // After the wait: the app itself scrolls on some steps (e.g. the e-mail stepper)
  await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' }))
  if (target && 'screenshot' in target) await expect(target).toHaveScreenshot(`${name}.png`)
  else await expect(page).toHaveScreenshot(`${name}.png`, { fullPage: !!target })
}

// ---------- sample data ----------

const positions = [
  ...data.positions,
  { id: 'pos-youth', name: 'Jugendleitung', isActive: true },
  { id: 'pos-trainer', name: 'Trainer/in', isActive: true },
  { id: 'pos-press', name: 'Öffentlichkeitsarbeit', isActive: true }
]
const holidays = [
  { id: 'h1', title: 'Herbstferien', startDate: '2026-10-12', endDate: '2026-10-24', sourceId: 's1', sourceName: 'Schulferien' },
  { id: 'h2', title: 'Weihnachtsferien', startDate: '2026-12-23', endDate: '2027-01-06', sourceId: 's1', sourceName: 'Schulferien' }
]
const clubs = [
  data.clubSummary({ departmentCount: 3 }),
  data.clubSummary({ id: ids.club2, name: 'Musikverein Harmonie', clubTypeId: 'ct-music', clubTypeName: 'Musikverein', email: 'info@harmonie.test', websiteUrl: 'https://harmonie.test', departmentCount: 1 }),
  data.clubSummary({ id: 'c-3', name: 'Freiwillige Feuerwehr Musterstadt', clubTypeId: null as never, clubTypeName: null as never, email: 'kontakt@ff-musterstadt.test', websiteUrl: null as never, departmentCount: 2 }),
  data.clubSummary({ id: 'c-4', name: 'Schützenverein 1888', clubTypeName: 'Sportverein', email: null as never, websiteUrl: null as never, departmentCount: 0 })
]
const people = {
  erika: { userId: erika.id, firstName: 'Erika', lastName: 'Muster', email: 'vorstand@tsv-musterstadt.de', phoneNumber: '0151 2345678', positionName: 'Vorsitz', canEdit: true, visibility: 1, contextEmail: 'vorstand@tsv-musterstadt.de', contextEmailVerified: true },
  max: { assignmentId: 'as-2', userId: 'u-max', firstName: 'Max', lastName: 'Beispiel', email: 'kasse@tsv-musterstadt.de', phoneNumber: null, contactPositionId: 'pos-treasurer', positionName: 'Kasse', canEdit: false, visibility: 1, contextEmail: 'kasse@tsv-musterstadt.de', contextEmailVerified: true },
  lea: { assignmentId: 'as-3', userId: 'u-lea', firstName: 'Lea', lastName: 'Lang', email: 'lea.lang@example.test', phoneNumber: null, contactPositionId: 'pos-youth', positionName: 'Jugendleitung', canEdit: true, visibility: 2, isConfirmed: false }
} satisfies Record<string, Partial<ContactAssignmentDto>>
const departments = [
  data.departmentSummary({ contactPersonsCount: 2, description: 'Vom Bambini bis zu den Alten Herren.', groupCount: 3 } as never),
  data.departmentSummary({ id: 'd-2', name: 'Turnen', contactPersonsCount: 1, description: 'Eltern-Kind-Turnen, Geräteturnen und Gymnastik.', groupCount: 2 } as never),
  data.departmentSummary({ id: 'd-3', name: 'Tischtennis', contactPersonsCount: 1, groupCount: 0 } as never)
]
const club = () => data.club({
  departmentCount: 3, departments,
  description: 'Der TSV Musterstadt ist ein Mehrspartenverein mit rund 800 Mitgliedern.\nNeue Mitglieder sind jederzeit willkommen: https://tsv.test/mitmachen',
  socialMediaLinks: [{ platform: 'instagram', url: 'https://www.instagram.com/tsv-musterstadt' }, { platform: 'facebook', url: 'https://www.facebook.com/tsv-musterstadt' }],
  locations: [{ id: 'loc-hall', name: 'Sporthalle Nord' }],
  contactAssignments: [data.assignment(people.erika), data.assignment(people.max), data.assignment(people.lea)]
} as never)
const groups = [
  data.departmentSummary({ id: ids.group, name: 'Bambini', contactPersonsCount: 1, isGroup: true } as never),
  data.departmentSummary({ id: 'g-2', name: 'D-Jugend', contactPersonsCount: 2, isGroup: true } as never),
  data.departmentSummary({ id: 'g-3', name: 'Alte Herren', contactPersonsCount: 1, isGroup: true } as never)
]
const football = () => data.department({
  description: 'Training für alle Altersklassen auf dem Sportplatz am Wald.', website: 'https://tsv.test/fussball', groups,
  contactAssignments: [data.assignment({ ...people.max, assignmentId: 'as-4', userId: 'u-tom', firstName: 'Tom', lastName: 'Tor', email: 'tom.tor@example.test', contextEmail: null, positionName: 'Abteilungsleitung', contactPositionId: null })]
} as never)
const bambini = () => data.department({
  id: ids.group, name: 'Bambini', isGroup: true, parentDepartmentId: ids.department, parentDepartmentName: 'Fußball', canDelete: false,
  description: 'Für Kinder von 4 bis 6 Jahren. Training samstags 10–11 Uhr.\nInfos unter https://tsv.test/bambini',
  contactAssignments: [data.assignment({ assignmentId: 'as-5', userId: 'u-tim', firstName: 'Tim', lastName: 'Trainer', email: 'tim@example.test', phoneNumber: null, positionName: 'Trainer/in' })]
})
const dashboard = {
  userDisplayName: 'Erika Muster',
  myClubs: [{
    id: ids.club, name: 'TSV Musterstadt', city: 'Musterstadt', isContactPerson: true, canEdit: true,
    departments: [{ id: ids.department, name: 'Fußball', isContactPerson: false, canEdit: true }, { id: ids.group, name: 'Bambini', parentDepartmentName: 'Fußball', isContactPerson: false, canEdit: true }]
  }]
}
const at = (day: number, hour: number, minutes = 0) => new Date(Date.UTC(2026, 10, day, hour - 1, minutes)).toISOString()
const events = [
  data.event({ id: 'ev-1', title: 'Jahreshauptversammlung', location: 'Vereinsheim', startUtc: at(27, 19), endUtc: at(27, 21) }),
  data.event({ id: 'ev-2', title: 'Weihnachtsmarkt', isHighlight: true, location: 'Marktplatz', clubName: 'Musikverein Harmonie', clubId: ids.club2, startUtc: at(29, 11), endUtc: at(29, 18) }),
  data.event({ id: 'ev-3', title: 'Hallenturnier D-Jugend', location: 'Sporthalle Nord', startUtc: at(14, 9), endUtc: at(14, 16) }),
  data.event({ id: 'ev-4', title: 'Adventskonzert', isHighlight: true, location: 'Stadtkirche', clubName: 'Musikverein Harmonie', clubId: ids.club2, startUtc: new Date(Date.UTC(2026, 11, 6, 16)).toISOString(), endUtc: new Date(Date.UTC(2026, 11, 6, 18)).toISOString() }),
  data.event({ id: 'ev-5', title: 'Übungsabend', location: 'Gerätehaus', clubName: 'Freiwillige Feuerwehr Musterstadt', clubId: 'c-3', startUtc: at(24, 19), endUtc: at(24, 21) })
]
const contacts = [
  data.contact({ id: erika.id, firstName: 'Erika', lastName: 'Muster', email: 'vorstand@tsv-musterstadt.de', phoneNumber: '0151 2345678', organizations: [{ organizationId: ids.club, organizationName: 'TSV Musterstadt', type: 0, positionName: 'Vorsitz' }] }),
  data.contact({ id: 'u-max', email: 'kasse@tsv-musterstadt.de', phoneNumber: null, organizations: [{ organizationId: ids.club, organizationName: 'TSV Musterstadt', type: 0, positionName: 'Kasse' }] }),
  data.contact({ id: 'u-lea', firstName: 'Lea', lastName: 'Lang', email: 'vorsitz@harmonie.test', phoneNumber: '02345 6789', organizations: [{ organizationId: ids.club2, organizationName: 'Musikverein Harmonie', type: 0, positionName: 'Vorsitz' }] }),
  data.contact({ id: 'u-tom', firstName: 'Tom', lastName: 'Tor', email: 'tom.tor@example.test', phoneNumber: null, organizations: [{ organizationId: ids.department, organizationName: 'Fußball', type: 1, parentName: 'TSV Musterstadt', positionName: 'Abteilungsleitung' }] }),
  data.contact({ id: 'u-tim', firstName: 'Tim', lastName: 'Trainer', email: 'tim@example.test', phoneNumber: null, organizations: [{ organizationId: ids.group, organizationName: 'Bambini', type: 1, parentName: 'TSV Musterstadt › Fußball', positionName: 'Trainer/in' }] }),
  data.contact({ id: 'u-kai', firstName: 'Kai', lastName: 'Kamerad', email: 'kontakt@ff-musterstadt.test', phoneNumber: null, organizations: [{ organizationId: 'c-3', organizationName: 'Freiwillige Feuerwehr Musterstadt', type: 0, positionName: 'Vorsitz' }] })
] as never[]

// ---------- Anmeldung & Zugang ----------

test('login-und-passkey', async ({ page, api }) => {
  api.post('/api/auth/login-link/request', null)
  await page.goto('/login')
  await shot(page, 'login')
  await page.getByLabel('E-Mail-Adresse').fill('erika.muster@web.de')
  await page.getByRole('button', { name: 'Anmeldelink senden' }).click()
  await page.getByText('Wir haben dir einen Anmeldelink').waitFor()
  await shot(page, 'login-link-gesendet')
})

test('einladung-und-registrierung', async ({ page, api, signIn }) => {
  const invitation = { email: 'erika.muster@web.de', expiresAt: '2026-12-10T00:00:00Z', privacyPolicyUrl: 'https://example.test/datenschutz', pendingAssignments: [{ invitationId: 'inv-1', clubName: 'TSV Musterstadt', positionName: 'Vorsitz', contextEmail: 'vorstand@tsv-musterstadt.de' }] }
  api.get('/api/invitations/details', invitation).post('/api/invitations/accept', { success: true, magicLinkSent: true })
  await page.goto('/onboarding?token=abc')
  await page.getByLabel('Vorname').fill('Erika')
  await page.getByLabel('Nachname').fill('Muster')
  await page.getByRole('checkbox', { name: /zur Kenntnis genommen/ }).check()
  await page.locator('.v-field').filter({ has: page.getByRole('combobox', { name: 'Zuordnung sichtbar für *', exact: true }) }).click()
  await page.locator('.v-overlay--active').getByRole('option', { name: /^Alle registrierten/ }).click()
  await shot(page, 'registrierung', { fullPage: true })
  await page.getByRole('button', { name: 'Registrierung abschließen' }).click()
  await page.getByText('Fast geschafft!').waitFor()
  await shot(page, 'registrierung-fast-geschafft')

  await signIn(erika, { hasPasskey: false })
  api.get('/api/users/me/contact-assignments', [data.myAssignment()])
  await page.goto('/willkommen')
  await shot(page, 'willkommen')
})

// ---------- Vernetzt nutzen ----------

test('navigation-und-dashboard', async ({ page, api, signIn }) => {
  await signIn()
  api.get('/api/events/public/global', events)
  await page.goto('/')
  await shot(page, 'dashboard')
  await page.getByRole('button', { name: 'Hilfe' }).click()
  await shot(page, 'hilfemenue')
})

test('vereine-uebersicht', async ({ page, api, signIn }) => {
  await signIn()
  api.get('/api/clubs/search', data.paged(clubs))
  await page.goto('/clubs')
  await shot(page, 'vereine-uebersicht')
})

test('vereinsdetails', async ({ page, api, signIn }) => {
  await signIn()
  api
    .get(`/api/clubs/${ids.club}`, club())
    .get('/api/users/contacts', [])
    .get(`/api/events/club/${ids.club}`, events.filter((e) => e.clubId === ids.club))
    .get(`/api/events/club/${ids.club}/paged`, data.paged(events.filter((e) => e.clubId === ids.club), 1, 20))
    .get('/api/calendar-feeds/tokens', [data.feedToken({ scope: 0, includeDepartments: true, clubId: ids.club } as never)])
  await page.goto(`/clubs/${ids.club}`)
  await shot(page, 'verein-uebersicht', { fullPage: true })

  await page.getByRole('main').getByRole('button', { name: 'Bearbeiten' }).last().click()
  await shot(page, 'verein-bearbeiten')
  await page.keyboard.press('Escape')

  await page.goto(`/clubs/${ids.club}?tab=contacts`)
  await shot(page, 'verein-kontaktpersonen')
  await page.getByRole('button', { name: /Kontaktperson hinzufügen/ }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('tab', { name: 'Neu einladen' }).click()
  await dialog.getByLabel('Persönliche E-Mail-Adresse *').fill('max.beispiel@gmx.de')
  await dialog.getByLabel('Amtsadresse für diese Zuordnung (optional)').fill('kasse@tsv-musterstadt.de')
  await dialog.locator('.v-field').filter({ has: page.getByRole('combobox', { name: 'Position (optional)', exact: true }) }).click()
  await page.locator('.v-overlay--active').getByRole('option', { name: 'Kasse' }).click()
  await shot(page, 'kontaktperson-einladen', dialog.locator('.v-card').first())
  await page.keyboard.press('Escape')

  await page.goto(`/clubs/${ids.club}?tab=events`)
  await shot(page, 'verein-termine', { fullPage: true })
})

test('abteilungsdetails-und-gruppen', async ({ page, api, signIn }) => {
  await signIn()
  api
    .get(`/api/departments/${ids.department}`, football())
    .get(`/api/departments/${ids.group}`, bambini())
    .get(`/api/events/department/${ids.department}/paged`, data.paged([], 1, 20))
    .get(`/api/events/department/${ids.group}/paged`, data.paged([], 1, 20))
  await page.goto(`/departments/${ids.department}`)
  await shot(page, 'abteilung', { fullPage: true })
  await page.goto(`/departments/${ids.group}`)
  await shot(page, 'gruppe')
})

test('kontakte', async ({ page, api, signIn }) => {
  await signIn()
  api.get('/api/users/contacts/search', data.paged(contacts))
  await page.goto('/kontakte')
  await shot(page, 'kontakte')
})

test('hauptkalender', async ({ page, api, signIn }) => {
  await signIn()
  api.get('/api/events/public/global', events).get('/api/calendar-feeds/tokens', [data.feedToken()])
  await page.goto('/calendar/main')
  await shot(page, 'kalender-monat')
  await page.getByRole('tab', { name: 'Liste' }).click()
  await shot(page, 'kalender-liste')
})

test('email-versenden', async ({ page, api, signIn }) => {
  await signIn()
  api.get('/api/users/recipient-candidates', [
    data.recipient({ assignmentId: 'as-1', userId: erika.id, firstName: 'Erika', lastName: 'Muster', displayEmail: 'vorstand@tsv-musterstadt.de', emailBadgeLabel: 'Amtsadresse', locationIds: ['loc-hall'] } as never),
    data.recipient({ assignmentId: 'as-2', userId: 'u-max', positionId: 'pos-treasurer', positionName: 'Kasse', displayEmail: 'kasse@tsv-musterstadt.de', emailBadgeLabel: 'Amtsadresse', locationIds: ['loc-hall'] } as never),
    data.recipient({ assignmentId: 'as-3', userId: 'u-tom', firstName: 'Tom', lastName: 'Tor', organizationId: ids.department, organizationName: 'Fußball', organizationType: 1, parentClubId: ids.club, parentClubName: 'TSV Musterstadt', positionId: null as never, positionName: 'Abteilungsleitung', displayEmail: 'tom.tor@example.test', locationIds: ['loc-hall'] } as never),
    data.recipient({ assignmentId: 'as-4', userId: 'u-lea', firstName: 'Lea', lastName: 'Lang', organizationId: ids.club2, organizationName: 'Musikverein Harmonie', clubTypeId: 'ct-music', displayEmail: 'vorsitz@harmonie.test', emailBadgeLabel: 'Amtsadresse', locationIds: [] } as never),
    data.recipient({ assignmentId: 'as-5', userId: 'u-kai', firstName: 'Kai', lastName: 'Kamerad', organizationId: 'c-3', organizationName: 'Freiwillige Feuerwehr Musterstadt', clubTypeId: null as never, displayEmail: 'kontakt@ff-musterstadt.test', locationIds: [] } as never)
  ])
  await page.goto('/emails/send')
  await page.getByText(/^Vorsitz \(\d+\)$/).click()
  await shot(page, 'email-filter')
  await page.getByRole('button', { name: 'Weiter', exact: true }).click()
  await shot(page, 'email-empfaenger')
  await page.getByRole('button', { name: /^Weiter \(\d+\)$/ }).click()
  await shot(page, 'email-versandart')
  await page.getByRole('button', { name: 'Weiter', exact: true }).click()
  await page.getByLabel('Betreff *').fill('Einladung zum Vereinsstammtisch')
  await page.locator('.tiptap').fill('Liebe Vorsitzende,\n\nwir laden euch herzlich zum nächsten Vereinsstammtisch ein.')
  await shot(page, 'email-verfassen')
})

test('mein-konto', async ({ page, api, signIn }) => {
  await signIn()
  await page.goto('/konto')
  await shot(page, 'konto-profil')

  await page.goto('/konto?tab=zuordnungen')
  await page.getByText('TSV Musterstadt').first().click()
  await shot(page, 'konto-zuordnungen')

  api.get('/api/users/me/contact-assignments', [data.myAssignment({ contextEmail: 'vorstand@tsv-musterstadt.de', contextEmailVerified: false })])
  await page.goto('/konto?tab=zuordnungen')
  await page.getByText('TSV Musterstadt').first().click()
  await shot(page, 'amtsadresse-bestaetigung-ausstehend')

  api.get('/api/users/me/contact-assignments', [data.myAssignment({ isConfirmed: false, visibility: 0, organizationName: 'Musikverein Harmonie', organizationId: ids.club2, positionName: 'Öffentlichkeitsarbeit' })])
  await page.goto('/konto?tab=zuordnungen&zuordnung=my-1')
  await shot(page, 'zuordnung-zustimmen')

  await page.goto('/konto?tab=sicherheit')
  await shot(page, 'konto-sicherheit', { fullPage: true })

  api.get('/api/users/me/carddav', { hasAccess: true, createdAt: '2026-11-02T08:00:00Z', lastUsedAt: '2026-11-19T18:30:00Z', serverUrl: 'https://vernetzt.gemeinde.test/carddav/', username: 'erika.muster@web.de', includeOrganizations: true })
  await page.goto('/konto?tab=adressbuch')
  await shot(page, 'konto-adressbuch')

  await page.goto('/konto?tab=datenschutz')
  await shot(page, 'konto-datenschutz')
})

// ---------- Administration ----------

test('admin', async ({ page, api, signIn }) => {
  await signIn(ada)
  api
    .get('/api/users', [
      { ...ada, passkeyCount: 2, lastLoginAt: '2026-11-20T08:00:00Z' },
      { ...erika, passkeyCount: 1, lastLoginAt: '2026-11-19T18:30:00Z' },
      data.user({ id: 'u-max', firstName: 'Max', lastName: 'Beispiel', email: 'max.beispiel@gmx.de', passkeyCount: 0, lastLoginAt: '2026-11-12T07:15:00Z' }),
      data.user({ id: 'u-mo', firstName: 'Mo', lastName: 'Manager', email: 'mo@gemeinde.test', claims: [1], passkeyCount: 1, lastLoginAt: '2026-11-18T09:00:00Z' }),
      data.user({ id: 'u-rita', firstName: 'Rita', lastName: 'Rathaus', email: 'rita@gemeinde.test', claims: [2], passkeyCount: 1, lastLoginAt: '2026-11-17T11:00:00Z' })
    ] as never)
    .get('/api/clubs', [data.club(), data.club({ id: ids.club2, name: 'Musikverein Harmonie' })])
    .get('/api/departments', [data.department()])
  await page.goto('/admin/users')
  await shot(page, 'admin-benutzer')
  await page.getByRole('button', { name: 'Person einladen' }).click()
  await page.getByRole('dialog').getByLabel('Persönliche E-Mail-Adresse *').fill('erika.muster@web.de')
  await shot(page, 'admin-einladen')
  await page.keyboard.press('Escape')

  api.get('/api/pending-items', {
    invitations: [{ id: 'i1', email: 'max.beispiel@gmx.de', invitedByName: 'Erika Muster', createdAt: '2026-11-18T10:00:00Z', expiresAt: '2026-11-25T10:00:00Z', clubId: ids.club, clubName: 'TSV Musterstadt', positionName: 'Kasse', contextEmail: 'kasse@tsv-musterstadt.de', pendingClaims: [] }],
    assignments: [{ assignmentId: 'as-3', userId: 'u-lea', userName: 'Lea Lang', userEmail: 'lea.lang@example.test', assignedByName: 'Erika Muster', assignedAt: '2026-11-16T09:00:00Z', lastConsentRequestAt: '2026-11-16T09:00:00Z', clubId: ids.club, clubName: 'TSV Musterstadt', positionName: 'Jugendleitung' }],
    contextEmails: [{ assignmentId: 'as-6', userId: 'u-tom', userName: 'Tom Tor', contextEmail: 'fussball@tsv-musterstadt.de', lastVerificationSentAt: '2026-11-15T12:00:00Z', isAssignmentConfirmed: true, clubName: 'TSV Musterstadt', departmentId: ids.department, departmentName: 'Fußball', positionName: 'Abteilungsleitung' }]
  })
  await page.goto('/admin/pending')
  await shot(page, 'admin-ausstehend')

  api.get('/api/clubtypes', [...data.clubTypes, { id: 'ct-fire', name: 'Feuerwehr', isActive: true }, { id: 'ct-social', name: 'Sozialverband', description: 'z. B. Tafel, Seniorenhilfe', isActive: false }])
  await page.goto('/admin/club-types')
  await shot(page, 'admin-vereinstypen')

  api.get('/api/contactpositions', positions)
  await page.goto('/admin/contact-positions')
  await shot(page, 'admin-kontaktpositionen')

  api.get('/api/locations', [
    { id: 'loc-hall', name: 'Sporthalle Nord', street: 'Nordring 5', postalCode: '12345', city: 'Musterstadt', description: 'Dreifachhalle', usageCount: 4 },
    { id: 'loc-pool', name: 'Hallenbad', street: 'Badstraße 2', postalCode: '12345', city: 'Musterstadt', description: null, usageCount: 1 },
    { id: 'loc-field', name: 'Sportplatz am Wald', street: 'Waldweg 1', postalCode: '12345', city: 'Musterstadt', description: 'Rasen- und Kunstrasenplatz', usageCount: 2 }
  ])
  await page.goto('/admin/locations')
  await shot(page, 'admin-standorte')

  api
    .get('/api/holidays', [...holidays, { id: 'h3', title: 'Kirmes', startDate: '2026-08-22', endDate: '2026-08-24', sourceId: null, sourceName: null }])
    .get('/api/holidays/sources', [{ id: 's1', name: 'Schulferien', url: 'https://example.org/schulferien.ics', isEnabled: true, lastSyncedAt: '2026-11-20T03:00:00Z', lastSyncError: null, entryCount: 12 }])
  await page.goto('/admin/holidays')
  await shot(page, 'admin-ferien')

  api
    .get('/api/settings', { contactName: 'Gemeindeverwaltung', contactEmail: 'vernetzt@gemeinde.test', privacyPolicyMode: 'link', privacyPolicyUrl: 'https://gemeinde.test/datenschutz' })
    .get('/api/calendar-feeds/tokens', [data.feedToken()])
    .get('/api/backups', { scheduleEnabled: true, scheduleTime: '03:00', timeZone: 'Europe/Berlin', nextRunUtc: '2026-11-21T02:00:00Z', keepDaily: 14, keepWeekly: 8, beforeMigration: true, directory: '/app/backups', lastFailureUtc: null, lastError: null, backups: [{ fileName: 'vernetzt-20261120-020000-scheduled.db', createdUtc: '2026-11-20T02:00:00Z', sizeBytes: 1468006, trigger: 0 }] })
    .get('/api/settings/portal', { title: 'Vereine in Musterstadt', subtitle: '', providerName: 'Gemeinde Musterstadt', providerUrl: 'https://gemeinde.test', noticeText: '', noticeLevel: 'info', hasLogo: false, imprint: { mode: 'url', html: '', url: 'https://gemeinde.test/impressum' }, privacy: { mode: 'url', html: '', url: 'https://gemeinde.test/datenschutz' } })
  await page.goto('/admin/settings')
  await shot(page, 'admin-einstellungen')

  api
    .get('/api/audit-logs', data.paged([
      { id: 'l1', entityType: 'Club', entityId: ids.club, entityDisplayName: 'TSV Musterstadt', action: 1, userId: erika.id, userName: 'Erika Muster', timestamp: '2026-11-20T09:12:00Z', ipAddress: '10.0.0.4' },
      { id: 'l2', entityType: 'Invitation', entityId: 'i1', entityDisplayName: 'max.beispiel@gmx.de', action: 0, userId: erika.id, userName: 'Erika Muster', timestamp: '2026-11-18T10:00:00Z', ipAddress: '10.0.0.4' },
      { id: 'l3', entityType: 'User', entityId: erika.id, entityDisplayName: 'Erika Muster', action: 7, userId: erika.id, userName: 'Erika Muster', timestamp: '2026-11-18T08:30:00Z', ipAddress: '10.0.0.4' },
      { id: 'l4', entityType: 'Event', entityId: 'ev-1', entityDisplayName: 'Jahreshauptversammlung', action: 0, userId: erika.id, userName: 'Erika Muster', timestamp: '2026-11-17T19:45:00Z', ipAddress: '10.0.0.4' }
    ], 1, 50))
    .get('/api/audit-logs/entity-types', ['Club', 'Event', 'Invitation', 'User'])
  await page.goto('/admin/audit-log')
  await shot(page, 'admin-audit-log')
})
