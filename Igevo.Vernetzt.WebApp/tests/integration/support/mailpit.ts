import { expect } from '@playwright/test'

export const mailpitUrl = (process.env.MAILPIT_URL ?? 'http://localhost:56080').replace(/\/$/, '')

interface MailpitSummary { ID: string; Subject: string; Created: string; To: Array<{ Address: string }> }
export interface Mail { id: string; subject: string; text: string; html: string; links: string[] }

/**
 * Waits for the newest e-mail to `to` that arrived after `since` (Mailpit is shared with development,
 * so older messages to the same address are ignored).
 */
export async function waitForMail(to: string, options: { since: Date; subject?: RegExp; timeout?: number }): Promise<Mail> {
  let found: MailpitSummary | undefined
  await expect.poll(async () => {
    const response = await fetch(`${mailpitUrl}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}&limit=20`)
    const result = await response.json() as { messages: MailpitSummary[] }
    found = result.messages.find((message) => new Date(message.Created) >= options.since && (!options.subject || options.subject.test(message.Subject)))
    return !!found
  }, { message: `E-Mail an ${to}${options.subject ? ` (${options.subject})` : ''} nicht in Mailpit gefunden`, timeout: options.timeout ?? 15_000, intervals: [250, 500, 1000] }).toBe(true)

  const message = await (await fetch(`${mailpitUrl}/api/v1/message/${found!.ID}`)).json() as { Subject: string; Text: string; HTML: string }
  const links = [...new Set([...(message.HTML ?? '').matchAll(/href=["']([^"']+)["']/g)].map((match) => (match[1] ?? '').replace(/&amp;/g, '&')).concat(message.Text.match(/https?:\/\/\S+/g) ?? []))]
  return { id: found!.ID, subject: message.Subject, text: message.Text, html: message.HTML, links }
}

/** First link in the mail whose path starts with `path` (e.g. "/login" or "/onboarding"). */
export function linkTo(mail: Mail, path: string): string {
  const link = mail.links.find((href) => { try { return new URL(href).pathname === path } catch { return false } })
  if (!link) throw new Error(`Kein Link auf ${path} in „${mail.subject}“. Gefunden: ${mail.links.join(', ')}`)
  return link
}
