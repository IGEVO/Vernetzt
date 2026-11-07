import type { AuditAction, AuditLogDto } from '@/types/api'

export const auditActions: Array<{ value: AuditAction; title: string; color: string }> = [
  { value: 0, title: 'Erstellt', color: 'success' },
  { value: 1, title: 'Geändert', color: 'warning' },
  { value: 2, title: 'Gelöscht', color: 'error' },
  { value: 3, title: 'Anmeldung', color: 'info' },
  { value: 4, title: 'Abmeldung', color: 'default' },
  { value: 5, title: 'Angesehen', color: 'secondary' },
  { value: 6, title: 'Relay weitergeleitet', color: 'info' },
  { value: 7, title: 'E-Mail versendet', color: 'primary' },
  { value: 8, title: 'Anmeldung fehlgeschlagen', color: 'error' },
  { value: 9, title: 'Relay abgewiesen', color: 'warning' }
]

export const auditAction = (value: AuditAction | string) =>
  auditActions.find((item) => item.value === Number(value)) ?? { value: Number(value) as AuditAction, title: String(value), color: 'default' }

const emptyGuid = '00000000-0000-0000-0000-000000000000'
const emailPattern = /[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi

/** Relay token addresses are secrets for the duration of their validity – never show them in overviews. */
function isRelayToken(value: string): boolean { return value.length >= 16 && value.length <= 64 && /^[0-9a-f]+$/i.test(value) }
export function redactRelayAddresses(input: string): string {
  return input.replace(emailPattern, (email) => {
    const [local, domain] = [email.slice(0, email.indexOf('@')), email.slice(email.indexOf('@') + 1)]
    const plus = local.indexOf('+')
    const relay = /relay/i.test(local) || /relay/i.test(domain) || isRelayToken(local) || (plus > 0 && isRelayToken(local.slice(plus + 1)))
    return relay ? '[Relay-Adresse]' : email
  })
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function entityName(entry: AuditLogDto): string {
  const name = redactRelayAddresses(entry.entityDisplayName ?? '').trim()
  if (!name || uuidPattern.test(name)) return entry.entityType
  return name.toLowerCase().startsWith('relay-token (') ? 'Relay-Token' : name
}
export function userName(entry: AuditLogDto): string {
  return entry.userName?.trim() || (!entry.userId || entry.userId === emptyGuid ? 'System' : 'Unbekannt')
}
export function note(entry: AuditLogDto): string {
  const value = entry.resolvedNotes ?? entry.notes
  return value?.trim() ? redactRelayAddresses(value).trim() || 'Siehe Details' : ''
}
export function formatJson(raw?: string | null): string {
  if (!raw?.trim()) return ''
  try { return JSON.stringify(JSON.parse(raw), null, 2) } catch { return raw }
}
export const isEmptyGuid = (value?: string | null) => !value || value === emptyGuid
