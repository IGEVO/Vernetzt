import { describe, expect, it } from 'vitest'
import { formatHolidayRange, holidaySegmentsForDay, holidaysForDay, yearRange } from '@/services/holidays'
import type { HolidayEntryDto } from '@/types/api'

const holidays: HolidayEntryDto[] = [
  { id: '1', title: 'Herbstferien', startDate: '2026-10-12', endDate: '2026-10-24' },
  { id: '2', title: 'Tag der Deutschen Einheit', startDate: '2026-10-03', endDate: '2026-10-03' }
]

describe('holidaysForDay', () => {
  it('includes the first and the last day', () => {
    expect(holidaysForDay(holidays, '2026-10-12').map((h) => h.title)).toEqual(['Herbstferien'])
    expect(holidaysForDay(holidays, '2026-10-24').map((h) => h.title)).toEqual(['Herbstferien'])
  })

  it('returns nothing outside the ranges', () => {
    expect(holidaysForDay(holidays, '2026-10-25')).toEqual([])
    expect(holidaysForDay(holidays, '2026-10-02')).toEqual([])
  })

  it('matches single-day entries', () => {
    expect(holidaysForDay(holidays, '2026-10-03').map((h) => h.title)).toEqual(['Tag der Deutschen Einheit'])
  })
})

describe('formatHolidayRange', () => {
  it('formats single days and ranges', () => {
    expect(formatHolidayRange(holidays[1]!)).toBe('03.10.2026')
    expect(formatHolidayRange(holidays[0]!)).toBe('12.10.2026 – 24.10.2026')
  })
})

describe('yearRange', () => {
  it('covers the whole year', () => {
    expect(yearRange(2027)).toEqual({ from: '2027-01-01', to: '2027-12-31' })
  })
})

describe('holidaySegmentsForDay', () => {
  const herbst = holidays[0]!
  const day = (value: string) => new Date(`${value}T00:00:00`)

  it('labels the first day and lets the label run to the end of the week row', () => {
    // Mon 12.10.2026
    expect(holidaySegmentsForDay([herbst], day('2026-10-12'))[0]).toMatchObject({ continuesFromLeft: false, continuesRight: true, showLabel: true, labelSpan: 7 })
  })

  it('continues without label in the middle of a week', () => {
    expect(holidaySegmentsForDay([herbst], day('2026-10-14'))[0]).toMatchObject({ continuesFromLeft: true, continuesRight: true, showLabel: false })
  })

  it('repeats the label at the start of the next week row and ends with the holiday', () => {
    // Mon 19.10.2026, holiday ends Sat 24.10.
    expect(holidaySegmentsForDay([herbst], day('2026-10-19'))[0]).toMatchObject({ continuesFromLeft: true, showLabel: true, labelSpan: 6 })
    expect(holidaySegmentsForDay([herbst], day('2026-10-24'))[0]).toMatchObject({ continuesRight: false, showLabel: false })
  })

  it('repeats the label on the first of a month and stops at its end', () => {
    const christmas: HolidayEntryDto = { id: 'w', title: 'Weihnachtsferien', startDate: '2026-12-23', endDate: '2027-01-06' }
    expect(holidaySegmentsForDay([christmas], day('2026-12-30'))[0]).toMatchObject({ showLabel: false })
    expect(holidaySegmentsForDay([christmas], day('2026-12-28'))[0]).toMatchObject({ showLabel: true, labelSpan: 4 })
    expect(holidaySegmentsForDay([christmas], day('2027-01-01'))[0]).toMatchObject({ continuesFromLeft: true, showLabel: true })
  })
})
