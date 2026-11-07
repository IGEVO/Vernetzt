import { describe, expect, it } from 'vitest'
import { backupTriggerLabel, currentBackupError, formatFileSize } from '@/services/backups'
import type { BackupStatusDto } from '@/types/api'

const status = (overrides: Partial<BackupStatusDto>): BackupStatusDto => ({
  scheduleEnabled: true,
  scheduleTime: '03:00',
  timeZone: 'Europe/Berlin',
  keepDaily: 14,
  keepWeekly: 8,
  beforeMigration: true,
  directory: '/app/backups',
  backups: [],
  ...overrides
})

describe('backupTriggerLabel', () => {
  it('accepts numeric and named enum values', () => {
    expect(backupTriggerLabel(0)).toBe('Automatisch')
    expect(backupTriggerLabel('Manual')).toBe('Manuell')
    expect(backupTriggerLabel(2)).toBe('Vor Update')
  })
})

describe('formatFileSize', () => {
  it('uses German number format', () => {
    expect(formatFileSize(512)).toBe('512 B')
    expect(formatFileSize(1536)).toBe('1,5 KB')
    expect(formatFileSize(1468006)).toBe('1,4 MB')
  })
})

describe('currentBackupError', () => {
  it('reports a failure newer than the latest backup', () => {
    expect(currentBackupError(status({ lastFailureUtc: '2026-11-05T02:00:00Z', lastError: 'disk full' }))).toBe('disk full')
  })

  it('ignores a failure followed by a successful backup', () => {
    expect(currentBackupError(status({
      lastFailureUtc: '2026-11-05T02:00:00Z',
      lastError: 'disk full',
      backups: [{ fileName: 'vernetzt-20261105-100000-manual.db', createdUtc: '2026-11-05T10:00:00Z', sizeBytes: 1, trigger: 1 }]
    }))).toBeNull()
  })
})
