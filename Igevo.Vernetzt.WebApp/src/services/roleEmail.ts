/**
 * Detects role / office addresses such as vorstand@verein.de or info@verein.de.
 *
 * A Vernetzt account belongs to a person, not to an office: role addresses are often handed over to the
 * successor, who would then gain access to the account. Such addresses belong in the context email of a
 * contact assignment instead. Detection is a heuristic for a soft warning only – it never blocks input.
 */

/** Words that mark a local part as an office rather than a person (lower case, umlauts transliterated). */
const ROLE_WORDS = new Set([
  'vorstand', 'vorsitz', 'vorsitzende', 'vorsitzender', 'praesident', 'praesidium', 'leitung', 'abteilungsleitung',
  'info', 'information', 'kontakt', 'contact', 'post', 'office', 'buero', 'sekretariat', 'geschaeftsstelle', 'verwaltung',
  'kasse', 'kassenwart', 'kassierer', 'schatzmeister', 'finanzen', 'beitrag', 'beitraege', 'mitglieder', 'mitgliederverwaltung',
  'jugend', 'jugendwart', 'jugendleitung', 'abteilung', 'sportwart', 'spielleiter', 'schriftfuehrer', 'schriftfuehrung',
  'presse', 'medien', 'oeffentlichkeitsarbeit', 'webmaster', 'admin', 'sponsoring', 'verein', 'hallo', 'noreply', 'no-reply'
])

function transliterate(value: string): string {
  return value.toLowerCase().replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue').replace(/ß/g, 'ss')
}

/** True if the address looks like a shared role / office mailbox (e.g. vorstand@, info@, jugend.fussball@). */
export function isRoleEmail(email: string | null | undefined): boolean {
  const value = email?.trim() ?? ''
  const at = value.lastIndexOf('@')
  if (at <= 0) return false
  const local = transliterate(value.slice(0, at))
  if (ROLE_WORDS.has(local)) return true
  // "1.vorsitzender", "jugend-u17", "kasse.tennis": any separated word counts
  return local.split(/[._+\-\d]+/).some((part) => ROLE_WORDS.has(part))
}

/** Same address ignoring case and surrounding whitespace. */
export function sameEmail(a: string | null | undefined, b: string | null | undefined): boolean {
  return !!a?.trim() && a.trim().toLowerCase() === b?.trim().toLowerCase()
}

export const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Link to the help page that explains personal vs. role addresses. */
export const ROLE_EMAIL_HELP_URL = '/help/app/amtsadressen/index.html'

/** Validation message for an optional role address next to the personal login address, or undefined if fine. */
export function contextEmailError(contextEmail: string | null | undefined, personalEmail: string | null | undefined): string | undefined {
  const value = contextEmail?.trim()
  if (!value) return undefined
  if (!EMAIL_PATTERN.test(value)) return 'Bitte eine gültige E-Mail-Adresse eingeben'
  if (sameEmail(value, personalEmail)) return 'Die Amtsadresse muss sich von der persönlichen Adresse unterscheiden'
  return undefined
}
