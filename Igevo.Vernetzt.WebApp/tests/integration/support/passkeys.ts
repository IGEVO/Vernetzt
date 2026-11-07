import type { CDPSession, Page } from '@playwright/test'

const sessions = new WeakMap<Page, { cdp: CDPSession; authenticatorId: string }>()

/**
 * Adds a Chromium virtual WebAuthn authenticator (platform authenticator with user verification),
 * so passkey registration and sign-in work without real hardware. Credentials live as long as the page.
 */
export async function addVirtualAuthenticator(page: Page): Promise<void> {
  const cdp = sessions.get(page)?.cdp ?? await page.context().newCDPSession(page)
  await cdp.send('WebAuthn.enable')
  const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true }
  })
  sessions.set(page, { cdp, authenticatorId })
}

/**
 * Simulates switching to another device: an authenticator refuses to register a second passkey for an account it
 * already holds one for (WebAuthn excludeCredentials), so a further passkey needs a fresh authenticator.
 */
export async function switchToNewVirtualAuthenticator(page: Page): Promise<void> {
  const current = sessions.get(page)
  if (current) await current.cdp.send('WebAuthn.removeVirtualAuthenticator', { authenticatorId: current.authenticatorId })
  await addVirtualAuthenticator(page)
}
