import type { BackupStatusDto, BackupTrigger } from '@/types/api'

const triggerLabels: Record<string, string> = {
  Scheduled: 'Automatisch',
  Manual: 'Manuell',
  PreMigration: 'Vor Update'
}
const triggerByNumber = ['Scheduled', 'Manual', 'PreMigration']

/** German label for what caused a backup (the API may send the enum as number or name). */
export function backupTriggerLabel(trigger: BackupTrigger): string {
  const name = typeof trigger === 'number' ? triggerByNumber[trigger] : trigger
  return (name && triggerLabels[name]) ?? 'Unbekannt'
}

/** File size in German notation, e.g. "1,4 MB". */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++ }
  return `${value.toLocaleString('de-DE', { maximumFractionDigits: 1 })} ${units[unit]}`
}

/** The last failed attempt, if it happened after the newest successful backup. */
export function currentBackupError(status: BackupStatusDto): string | null {
  if (!status.lastFailureUtc) return null
  const latest = status.backups[0]
  if (latest && new Date(latest.createdUtc) > new Date(status.lastFailureUtc)) return null
  return status.lastError || 'Die letzte Sicherung ist fehlgeschlagen.'
}
