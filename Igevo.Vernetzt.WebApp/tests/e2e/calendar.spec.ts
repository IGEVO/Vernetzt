import { data, expect, snackbar, test } from './support/fixtures'

const { ids } = data

test.describe('Hauptkalender', () => {
  test('zeigt kommende Termine und filtert nach Highlights', async ({ page, api, signIn }) => {
    await signIn()
    api.get('/api/events/public/global', [
      data.event(),
      data.event({ id: 'ev-2', title: 'Sommerfest', isHighlight: true })
    ])
    api.get('/api/calendar-feeds/tokens', [])
    await page.goto('/calendar/main')
    await expect(page.getByRole('heading', { name: 'Kalender', level: 1 })).toBeVisible()
    await page.getByRole('tab', { name: 'Liste' }).click()
    await expect(page.getByText('Jahreshauptversammlung')).toBeVisible()
    await expect(page.getByText('Sommerfest')).toBeVisible()

    await page.getByLabel('Nur Highlights').check()
    await expect(page.getByText('Jahreshauptversammlung')).toHaveCount(0)
    await expect(page.getByText('Sommerfest')).toBeVisible()
    // Without active global subscription links the iCal tab stays hidden.
    await expect(page.getByRole('tab', { name: 'iCal-Abos' })).toHaveCount(0)
  })

  test('zeigt aktive globale Abo-Links zum Kopieren', async ({ page, api, signIn }) => {
    await signIn()
    api
      .get('/api/events/public/global', [data.event()])
      .get('/api/calendar-feeds/tokens', [data.feedToken(), data.feedToken({ id: 'feed-2', highlightsOnly: true, isRevoked: true })])
    await page.goto('/calendar/main')

    await page.getByRole('tab', { name: 'iCal-Abos' }).click()
    const panel = page.locator('.v-card', { hasText: 'Kalender abonnieren (iCal)' })
    await expect(panel.getByText('Alle Termine', { exact: true })).toBeVisible()
    await expect(panel.getByRole('textbox')).toHaveValue(data.feedToken().feedUrl!)
    // Revoked links are not offered.
    await expect(panel.getByText('Nur Highlights')).toHaveCount(0)
  })

  test('zeigt Ferien und Feiertage als Hintergrund im Monatsraster', async ({ page, api, signIn }) => {
    await page.clock.setFixedTime(new Date('2026-10-15T10:00:00'))
    await signIn()
    api
      .get('/api/events/public/global', [])
      .get('/api/calendar-feeds/tokens', [])
      .get('/api/holidays', [
        { id: 'h1', title: 'Herbstferien', startDate: '2026-10-12', endDate: '2026-10-24', sourceId: 's1', sourceName: 'Schulferien' },
        { id: 'h2', title: 'Tag der Deutschen Einheit', startDate: '2026-10-03', endDate: '2026-10-03' }
      ])
    await page.goto('/calendar/main')

    // One band segment per day; the name only where a band starts (first day and each new week row).
    await expect(page.locator('.calendar-holiday')).toHaveCount(14)
    await expect(page.locator('.calendar-holiday-label', { hasText: 'Tag der Deutschen Einheit' })).toHaveCount(1)
    await expect(page.locator('.calendar-holiday-label', { hasText: 'Herbstferien' })).toHaveCount(2)
    const call = api.callsTo('GET', '/api/holidays')[0]!
    expect(call.query.get('from')).toBe('2026-01-01')
    expect(call.query.get('to')).toBe('2026-12-31')
  })
})

test.describe('Termine eines Vereins', () => {
  test.beforeEach(async ({ api, signIn }) => {
    await signIn()
    api
      .get(`/api/clubs/${ids.club}`, data.club())
      .get(`/api/events/club/${ids.club}`, [data.event()])
      .get(`/api/events/club/${ids.club}/paged`, data.paged([data.event()], 1, 20))
      .get('/api/calendar-feeds/tokens', [])
  })

  test('Termin anlegen', async ({ page, api }) => {
    api.post('/api/events', ({ body }) => data.event({ ...(body as object), id: 'ev-new' }))
    await page.goto(`/clubs/${ids.club}?tab=events`)
    await page.getByRole('button', { name: 'Termin anlegen' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Titel *').fill('Sommerfest')
    await dialog.getByLabel('Beginn *').fill('2026-07-04T14:00')
    await dialog.getByLabel('Ende *').fill('2026-07-04T20:00')
    await dialog.getByLabel('Ort').fill('Sportplatz')
    await dialog.getByRole('button', { name: 'Speichern' }).click()

    await expect(snackbar(page)).toContainText('Termin wurde gespeichert.')
    expect(api.lastBody('POST', '/api/events')).toMatchObject({
      title: 'Sommerfest', location: 'Sportplatz', isAllDay: false, clubId: ids.club,
      startUtc: '2026-07-04T12:00:00.000Z', endUtc: '2026-07-04T18:00:00.000Z'
    })
  })

  test('iCal-Link erstellen', async ({ page, api }) => {
    api.post('/api/calendar-feeds/tokens', { token: data.feedToken({ scope: 0, includeDepartments: false }), feedUrl: 'x' })
    await page.goto(`/clubs/${ids.club}?tab=events`)
    await page.getByRole('tab', { name: 'iCal-Abos' }).click()
    await page.getByRole('button', { name: 'Link erstellen' }).first().click()
    expect(api.lastBody('POST', '/api/calendar-feeds/tokens')).toEqual({ scope: 0, includeDepartments: false, clubId: ids.club, departmentId: null, highlightsOnly: false })
  })
})
