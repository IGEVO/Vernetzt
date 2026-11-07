import { describe, expect, it } from 'vitest'
import { berlinDayBoundaryUtc, formatEventDate } from '../app/utils/date'

describe('event dates', () => {
  it('formats UTC timestamps in Europe/Berlin', () => {
    expect(formatEventDate('2026-11-06T17:00:00Z', '2026-11-06T19:00:00Z', false))
      .toContain('18:00–20:00 Uhr')
  })

  it('omits times for all-day events', () => {
    expect(formatEventDate('2026-11-06T00:00:00Z', '2026-11-06T23:00:00Z', true)).not.toContain('Uhr')
  })

  it('uses the correct Berlin daylight-saving offset for date filters', () => {
    expect(berlinDayBoundaryUtc('2026-01-15')).toBe('2026-01-14T23:00:00.000Z')
    expect(berlinDayBoundaryUtc('2026-07-15')).toBe('2026-07-14T22:00:00.000Z')
  })
})
