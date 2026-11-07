function localParts(value: Date): string {
  const pad = (part: number) => String(part).padStart(2, '0')
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}T${pad(value.getHours())}:${pad(value.getMinutes())}`
}

export function utcToLocalInput(value: string): string { return localParts(new Date(value)) }
export function utcToLocalDate(value: string): string { return localParts(new Date(value)).slice(0, 10) }

export function eventRangeToUtc(start: string, end: string, allDay: boolean): { startUtc: string; endUtc: string } {
  if (!allDay) return { startUtc: new Date(start).toISOString(), endUtc: new Date(end).toISOString() }
  const startLocal = new Date(`${start}T00:00:00`)
  const endExclusiveLocal = new Date(`${end}T00:00:00`)
  endExclusiveLocal.setDate(endExclusiveLocal.getDate() + 1)
  return { startUtc: startLocal.toISOString(), endUtc: endExclusiveLocal.toISOString() }
}

export function allDayInclusiveEnd(value: string): string {
  const end = new Date(value)
  end.setDate(end.getDate() - 1)
  return localParts(end).slice(0, 10)
}

const dateTimeFormatter = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' })
const dateFormatter = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })

/** Uniform display format for timestamps, e.g. "04.11.2026, 14:30". */
export function formatDateTime(value: string | Date | null | undefined): string {
  if (!value) return '–'
  const date = value instanceof Date ? value : new Date(value)
  return Number.isNaN(date.getTime()) ? '–' : dateTimeFormatter.format(date)
}

export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return '–'
  const date = value instanceof Date ? value : new Date(value)
  return Number.isNaN(date.getTime()) ? '–' : dateFormatter.format(date)
}
