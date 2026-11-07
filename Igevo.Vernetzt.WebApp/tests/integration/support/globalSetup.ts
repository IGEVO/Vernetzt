import { mailpitUrl } from './mailpit'

/** Fails fast with a helpful message when Mailpit is not reachable (before servers are started). */
export default async function globalSetup(): Promise<void> {
  try {
    const response = await fetch(`${mailpitUrl}/api/v1/info`, { signal: AbortSignal.timeout(3000) })
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
  } catch (error) {
    throw new Error(
      `Mailpit ist unter ${mailpitUrl} nicht erreichbar (${String(error)}).\n` +
      'Starte Mailpit z. B. über das Aspire-AppHost oder mit `docker run -p 56025:1025 -p 56080:8025 axllent/mailpit`.\n' +
      'Abweichende Ports: MAILPIT_URL und MAILPIT_SMTP_PORT setzen.',
      { cause: error }
    )
  }
}
