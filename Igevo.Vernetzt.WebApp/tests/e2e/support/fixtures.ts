import { test as base, expect, type Locator, type Page } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { MockApi } from './mockApi'
import * as data from './data'
import type { UserDto } from '../../../src/types/api'

export { expect, data }
export { fail, raw } from './mockApi'

interface Fixtures {
  /** Mocked API, installed before every test. Signed out by default – call `signIn()`. */
  api: MockApi
  /** Signs in the given user (defaults to a regular user without roles) and mocks the session-wide endpoints. */
  signIn: (user?: UserDto, options?: { hasPasskey?: boolean }) => Promise<UserDto>
}

/** Session-independent defaults every page may load. */
function installDefaults(api: MockApi): void {
  api
    .get('/api/auth/status', { isAuthenticated: false })
    .get('/api/clubtypes/active', data.clubTypes)
    .get('/api/contactpositions/active', data.positions)
    .get('/api/locations/active', data.locations)
    .get('/api/email/relay/status', true)
}

export const test = base.extend<Fixtures>({
  api: async ({ page }, use) => {
    // Conditional passkey UI would otherwise start a background WebAuthn request on the login page.
    await page.addInitScript(() => {
      if ('PublicKeyCredential' in window) Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', { value: async () => false })
    })
    const api = new MockApi()
    installDefaults(api)
    await api.install(page)
    await use(api)
    expect(api.unhandled, `Nicht gemockte API-Aufrufe: ${api.unhandled.join(', ')}`).toEqual([])
  },
  signIn: async ({ api }, use) => {
    await use(async (user = data.user(), options = {}) => {
      api
        .get('/api/auth/status', { isAuthenticated: true, userId: user.id, hasPasskey: options.hasPasskey ?? true, passkeyRequired: false })
        .get('/api/users/me', user)
        .get('/api/auth/passkeys', options.hasPasskey === false ? [] : [{ credentialId: 'cred-1', name: 'MacBook', createdAt: '2026-10-01T08:00:00Z' }])
        // Holiday layer of the calendar views; tests override it where they check it.
        .get('/api/holidays', [])
        .get('/api/stats/dashboard', { userDisplayName: `${user.firstName} ${user.lastName}`, myClubs: [] })
      return user
    })
  }
})

/** Fails on serious or critical accessibility violations (axe) inside `selector`. */
export async function expectAccessible(page: Page, selector = 'body'): Promise<void> {
  const results = await new AxeBuilder({ page }).include(selector).analyze()
  const serious = results.violations.filter((item) => ['critical', 'serious'].includes(item.impact ?? ''))
  expect(serious.map((item) => `${item.id}: ${item.help} (${item.nodes.length}x)`)).toEqual([])
}

/** Snackbar text (global notifications). */
export const snackbar = (page: Page) => page.locator('.v-snackbar__content')

/** Opens a Vuetify select/autocomplete by its label and picks an option (the native input itself is not clickable). */
export async function selectOption(page: Page, scope: Page | Locator, label: string, option: string | RegExp): Promise<void> {
  await scope.locator('.v-field').filter({ has: page.getByRole('combobox', { name: label, exact: true }) }).click()
  await page.getByRole('option', { name: option }).click()
}
