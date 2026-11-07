const dateFormatter = new Intl.DateTimeFormat('de-DE', {
  timeZone: 'Europe/Berlin',
  weekday: 'short',
  day: '2-digit',
  month: 'long',
  year: 'numeric'
})

const timeFormatter = new Intl.DateTimeFormat('de-DE', {
  timeZone: 'Europe/Berlin',
  hour: '2-digit',
  minute: '2-digit'
})

export function formatEventDate(startUtc?: string, endUtc?: string, allDay?: boolean): string {
  if (!startUtc) return ''
  const start = new Date(startUtc)
  const end = endUtc ? new Date(endUtc) : start
  const sameDay = dateFormatter.format(start) === dateFormatter.format(end)

  if (allDay) {
    return sameDay ? dateFormatter.format(start) : `${dateFormatter.format(start)} – ${dateFormatter.format(end)}`
  }

  return sameDay
    ? `${dateFormatter.format(start)}, ${timeFormatter.format(start)}–${timeFormatter.format(end)} Uhr`
    : `${dateFormatter.format(start)}, ${timeFormatter.format(start)} Uhr – ${dateFormatter.format(end)}, ${timeFormatter.format(end)} Uhr`
}

export function todayIsoDate(): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Berlin', year: 'numeric', month: '2-digit', day: '2-digit'
  }).formatToParts(new Date())
  const get = (type: string) => parts.find(part => part.type === type)?.value ?? ''
  return `${get('year')}-${get('month')}-${get('day')}`
}

export function berlinDayBoundaryUtc(date: string, endOfDay = false): string | undefined {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date)
  if (!match) return undefined
  const [, year, month, day] = match
  const hour = endOfDay ? 23 : 0
  const minute = endOfDay ? 59 : 0
  const second = endOfDay ? 59 : 0
  const desiredAsUtc = Date.UTC(Number(year), Number(month) - 1, Number(day), hour, minute, second)

  const formatter = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Berlin', year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23'
  })
  const represented = Object.fromEntries(
    formatter.formatToParts(new Date(desiredAsUtc))
      .filter(part => part.type !== 'literal')
      .map(part => [part.type, part.value])
  )
  const representedAsUtc = Date.UTC(
    Number(represented.year), Number(represented.month) - 1, Number(represented.day),
    Number(represented.hour), Number(represented.minute), Number(represented.second)
  )
  const offset = representedAsUtc - desiredAsUtc
  return new Date(desiredAsUtc - offset).toISOString()
}

const dayFormatter = new Intl.DateTimeFormat('de-DE', { timeZone: 'Europe/Berlin', day: 'numeric' })
const shortMonthFormatter = new Intl.DateTimeFormat('de-DE', { timeZone: 'Europe/Berlin', month: 'short' })
const monthFormatter = new Intl.DateTimeFormat('de-DE', { timeZone: 'Europe/Berlin', month: 'long', year: 'numeric' })
const weekdayFormatter = new Intl.DateTimeFormat('de-DE', { timeZone: 'Europe/Berlin', weekday: 'short' })

/** Day and short month for the calendar block, e.g. { day: '7', month: 'Nov' }. */
export function dateBlockParts(startUtc?: string): { day: string; month: string } {
  if (!startUtc) return { day: '', month: '' }
  const start = new Date(startUtc)
  return { day: dayFormatter.format(start), month: shortMonthFormatter.format(start).replace('.', '') }
}

/** Time part for list rows: weekday plus time span, or "ganztägig". Multi-day events show the end date. */
export function formatEventTime(startUtc?: string, endUtc?: string, allDay?: boolean): string {
  if (!startUtc) return ''
  const start = new Date(startUtc)
  const end = endUtc ? new Date(endUtc) : start
  const weekday = weekdayFormatter.format(start).replace('.', '')
  const sameDay = dateFormatter.format(start) === dateFormatter.format(end)
  if (!sameDay) return `${weekday} bis ${dateFormatter.format(end)}`
  if (allDay) return `${weekday}, ganztägig`
  return `${weekday}, ${timeFormatter.format(start)}–${timeFormatter.format(end)} Uhr`
}

/** Month heading for grouped event lists, e.g. "November 2026". */
export function monthLabel(startUtc?: string): string {
  return startUtc ? monthFormatter.format(new Date(startUtc)) : ''
}
