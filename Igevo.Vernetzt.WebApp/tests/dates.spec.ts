import { describe, expect, it } from 'vitest'
import { allDayInclusiveEnd, eventRangeToUtc, utcToLocalInput } from '@/services/dates'

describe('Termindaten', () => {
  it('konvertiert lokale Eingaben in UTC und zurück', () => {
    const range = eventRangeToUtc('2026-10-28T09:30', '2026-10-28T10:30', false)
    expect(utcToLocalInput(range.startUtc)).toBe('2026-10-28T09:30')
  })

  it('speichert ganztägige Enddaten exklusiv', () => {
    const range = eventRangeToUtc('2026-10-28', '2026-10-30', true)
    expect(allDayInclusiveEnd(range.endUtc)).toBe('2026-10-30')
  })
})
