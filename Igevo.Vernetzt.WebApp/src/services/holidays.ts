import type { HolidayEntryDto } from '@/types/api'

/** Holidays covering the given day (yyyy-MM-dd); ISO dates compare correctly as strings. */
export function holidaysForDay(holidays: readonly HolidayEntryDto[], dateStr: string): HolidayEntryDto[] {
  return holidays.filter((holiday) => holiday.startDate <= dateStr && holiday.endDate >= dateStr)
}

/** Request range for one calendar year (yyyy-MM-dd, both inclusive). */
export function yearRange(year: number): { from: string; to: string } {
  return { from: `${year}-01-01`, to: `${year}-12-31` }
}

const dayFormatter = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' })

function parseDate(value: string): Date {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(year!, month! - 1, day!)
}

/** "03.10.2026" or "12.10.2026 – 24.10.2026". */
export function formatHolidayRange(holiday: Pick<HolidayEntryDto, 'startDate' | 'endDate'>): string {
  const start = dayFormatter.format(parseDate(holiday.startDate))
  return holiday.startDate === holiday.endDate ? start : `${start} – ${dayFormatter.format(parseDate(holiday.endDate))}`
}

/** One day's slice of a holiday band in the month grid. */
export interface HolidaySegment {
  holiday: HolidayEntryDto
  /** The holiday started on an earlier day (no rounded left edge). */
  continuesFromLeft: boolean
  /** The holiday goes on the next day (no rounded right edge). */
  continuesRight: boolean
  /** The name is shown where the band starts: first day, every Monday and the 1st of the month. */
  showLabel: boolean
  /** Days the label may run across: until the holiday, the week row or the month ends. */
  labelSpan: number
}

function toDateStr(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** Band segments for a day of the Monday-first month grid, in a stable order (start date, then title). */
export function holidaySegmentsForDay(holidays: readonly HolidayEntryDto[], date: Date): HolidaySegment[] {
  const dateStr = toDateStr(date)
  const weekdayIndex = (date.getDay() + 6) % 7
  const lastDayOfMonth = new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate()
  const rowStart = weekdayIndex === 0 || date.getDate() === 1

  return holidaysForDay(holidays, dateStr)
    .slice()
    .sort((a, b) => a.startDate.localeCompare(b.startDate) || a.title.localeCompare(b.title))
    .map((holiday) => {
      const end = parseDate(holiday.endDate)
      const daysToEnd = Math.round((end.getTime() - parseDate(dateStr).getTime()) / 86_400_000) + 1
      return {
        holiday,
        continuesFromLeft: holiday.startDate < dateStr,
        continuesRight: holiday.endDate > dateStr,
        showLabel: holiday.startDate === dateStr || rowStart,
        labelSpan: Math.max(1, Math.min(daysToEnd, 7 - weekdayIndex, lastDayOfMonth - date.getDate() + 1))
      }
    })
}
