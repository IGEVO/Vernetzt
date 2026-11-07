<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { holidaySegmentsForDay, type HolidaySegment } from '@/services/holidays'
import type { EventDto, HolidayEntryDto } from '@/types/api'

const props = defineProps<{
  events: EventDto[]
  canEdit?: boolean
  /** Central school/public holidays, shown as a background layer (not clickable, not events). */
  holidays?: HolidayEntryDto[]
}>()

const emit = defineEmits<{
  (e: 'click-event', event: EventDto): void
  (e: 'new-event', date: string): void
  (e: 'view-month-change', payload: { year: number; month: number }): void
}>()

const today = new Date()
const viewYear = ref(today.getFullYear())
const viewMonth = ref(today.getMonth())

const monthLabel = computed(() =>
  new Intl.DateTimeFormat('de-DE', { month: 'long', year: 'numeric' }).format(
    new Date(viewYear.value, viewMonth.value, 1)
  )
)

function prevMonth(): void {
  if (viewMonth.value === 0) { viewMonth.value = 11; viewYear.value-- }
  else { viewMonth.value-- }
}

function nextMonth(): void {
  if (viewMonth.value === 11) { viewMonth.value = 0; viewYear.value++ }
  else { viewMonth.value++ }
}

function goToToday(): void {
  viewYear.value = today.getFullYear()
  viewMonth.value = today.getMonth()
}

watch([viewYear, viewMonth], () => {
  emit('view-month-change', { year: viewYear.value, month: viewMonth.value + 1 })
}, { immediate: true })

function toDateStr(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

const calendarDays = computed(() => {
  const year = viewYear.value
  const month = viewMonth.value
  const firstDay = new Date(year, month, 1)
  const lastDay = new Date(year, month + 1, 0)
  const startOffset = (firstDay.getDay() + 6) % 7
  const cells: Array<{ date: Date | null; dateStr: string | null }> = []
  for (let i = 0; i < startOffset; i++) cells.push({ date: null, dateStr: null })
  for (let d = 1; d <= lastDay.getDate(); d++) {
    const date = new Date(year, month, d)
    cells.push({ date, dateStr: toDateStr(date) })
  }
  while (cells.length % 7 !== 0) cells.push({ date: null, dateStr: null })
  return cells
})

function holidaySegments(date: Date | null): HolidaySegment[] {
  if (!date || !props.holidays?.length) return []
  return holidaySegmentsForDay(props.holidays, date)
}

function holidayBandClass(segment: HolidaySegment): string[] {
  return [
    segment.continuesFromLeft ? 'calendar-holiday--from-left' : '',
    segment.continuesRight ? 'calendar-holiday--to-right' : ''
  ]
}

/** The label may run across the following cells of the same band (cell width plus gutters). */
function holidayLabelStyle(segment: HolidaySegment): Record<string, string> {
  return { maxWidth: `calc(${segment.labelSpan * 100}% + ${(segment.labelSpan - 1) * 9}px)` }
}

function isToday(dateStr: string | null): boolean {
  return !!dateStr && dateStr === toDateStr(today)
}

// ─── All-day span logic ────────────────────────────────────────────────────

interface DayEventEntry {
  event: EventDto
  /** starts on or before this day, and the event started on an earlier day */
  continuesFromLeft: boolean
  /** ends on or after tomorrow, event continues to next day(s) */
  continuesRight: boolean
  /** first day of span visible in this cell */
  isStart: boolean
}

function parseDay(dateStr: string): Date {
  return new Date(`${dateStr}T00:00:00`)
}

function eventsForDay(dateStr: string | null): DayEventEntry[] {
  if (!dateStr) return []
  const dayStart = parseDay(dateStr)
  const dayEnd = new Date(dayStart)
  dayEnd.setDate(dayEnd.getDate() + 1) // exclusive end

  return props.events
    .filter((ev) => {
      const s = new Date(ev.startUtc)
      const e = new Date(ev.endUtc)
      return s < dayEnd && e > dayStart
    })
    .map((ev) => {
      const evStart = new Date(ev.startUtc)
      const evEnd = new Date(ev.endUtc)
      const continuesFromLeft = evStart < dayStart
      const continuesRight = evEnd > dayEnd
      const isStart = !continuesFromLeft
      return { event: ev, continuesFromLeft, continuesRight, isStart }
    })
    .sort((a, b) => a.event.startUtc.localeCompare(b.event.startUtc))
}

// ─── Styling helpers ────────────────────────────────────────────────────────

function eventColor(ev: EventDto): string {
  return ev.isHighlight ? 'secondary' : 'primary'
}

function formatEventTime(ev: EventDto): string {
  if (ev.isAllDay) return ''
  return new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' }).format(
    new Date(ev.startUtc)
  )
}

function spanClass(entry: DayEventEntry): string {
  if (!entry.event.isAllDay) return 'event-chip--timed'
  if (entry.continuesFromLeft && entry.continuesRight) return 'event-chip--all-day event-chip--middle'
  if (entry.continuesFromLeft) return 'event-chip--all-day event-chip--end'
  if (entry.continuesRight) return 'event-chip--all-day event-chip--start'
  return 'event-chip--all-day'
}

const weekDays = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So']

function handleDayClick(dateStr: string | null): void {
  if (!dateStr || !props.canEdit) return
  emit('new-event', dateStr)
}
</script>

<template>
  <div class="event-calendar">
    <!-- Navigation header -->
    <div class="d-flex align-center mb-3 gap-2">
      <v-btn icon="mdi-chevron-left" variant="text" density="comfortable" aria-label="Vorheriger Monat" @click="prevMonth" />
      <span class="text-title-medium font-weight-medium flex-grow-1 text-center">{{ monthLabel }}</span>
      <v-btn icon="mdi-chevron-right" variant="text" density="comfortable" aria-label="Nächster Monat" @click="nextMonth" />
      <v-btn variant="tonal" size="small" @click="goToToday">Heute</v-btn>
    </div>

    <!-- Weekday header row -->
    <div class="calendar-grid">
      <div
        v-for="day in weekDays"
        :key="day"
        class="calendar-header text-body-small font-weight-bold text-medium-emphasis text-center py-1"
      >
        {{ day }}
      </div>

      <!-- Day cells -->
      <div
        v-for="(cell, idx) in calendarDays"
        :key="idx"
        class="calendar-cell"
        :class="{
          'calendar-cell--empty': !cell.date,
          'calendar-cell--today': isToday(cell.dateStr),
          'calendar-cell--holiday': holidaySegments(cell.date).length > 0,
          'calendar-cell--clickable': !!cell.date && canEdit
        }"
        @click="handleDayClick(cell.dateStr)"
      >
        <template v-if="cell.date">
          <!-- Day number -->
          <span
            class="calendar-day-number"
            :class="isToday(cell.dateStr) ? 'bg-primary text-white rounded-circle' : ''"
          >
            {{ cell.date.getDate() }}
          </span>

          <!-- Holidays (background layer) -->
          <div
            v-for="segment in holidaySegments(cell.date)"
            :key="segment.holiday.id"
            class="calendar-holiday"
            :class="holidayBandClass(segment)"
            :title="segment.holiday.title"
          >
            <span v-if="segment.showLabel" class="calendar-holiday-label" :style="holidayLabelStyle(segment)">
              {{ segment.holiday.title }}
            </span>
          </div>

          <!-- Events -->
          <div class="calendar-events">
            <div
              v-for="entry in eventsForDay(cell.dateStr)"
              :key="entry.event.id"
              class="calendar-event-chip"
              :class="[spanClass(entry), `event-chip--color-${eventColor(entry.event)}`]"
              role="button"
              tabindex="0"
              @click.stop="emit('click-event', entry.event)"
              @keydown.enter="emit('click-event', entry.event)"
            >
              <v-icon v-if="entry.event.isHighlight && entry.isStart" size="9" icon="mdi-star" class="mr-1 flex-shrink-0" />
              <span v-if="entry.isStart || !entry.event.isAllDay" class="event-chip-text">
                {{ formatEventTime(entry.event) }} {{ entry.event.title }}
              </span>
              <v-icon v-if="entry.continuesRight && entry.event.isAllDay" size="10" icon="mdi-chevron-right" class="ml-auto flex-shrink-0" />
            </div>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
.calendar-grid {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 8px;
  overflow: hidden;
}

.calendar-header {
  border-right: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  background-color: rgba(var(--v-theme-surface-variant), 0.4);
  padding: 4px 6px;
}

.calendar-header:last-child {
  border-right: none;
}

.calendar-cell {
  border-right: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  min-height: 96px;
  padding: 4px 4px 4px 4px;
  position: relative;
  vertical-align: top;
}

.calendar-cell:nth-child(7n + 1):not(.calendar-header) {
  /* First column — no special treatment needed */
}

/* Remove right border on last column */
.calendar-cell:nth-child(7n) {
  border-right: none;
}

/* Remove bottom border on last row */
.calendar-cell:nth-last-child(-n+7) {
  border-bottom: none;
}

.calendar-cell--empty {
  background-color: rgba(var(--v-border-color), 0.03);
}

/* ── Holiday bands (background layer, spans multiple days) ─────────────── */
.calendar-holiday {
  display: flex;
  align-items: center;
  height: 14px;
  margin-bottom: 2px;
  padding: 0 5px;
  border-radius: 7px;
  background-color: rgba(var(--v-theme-info), 0.12);
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
  font-size: 0.65rem;
  line-height: 14px;
}
.calendar-holiday--from-left {
  border-top-left-radius: 0;
  border-bottom-left-radius: 0;
  margin-left: -4px;
}
.calendar-holiday--to-right {
  border-top-right-radius: 0;
  border-bottom-right-radius: 0;
  margin-right: -4px;
}
.calendar-holiday-label {
  /* Runs across the following cells of the band; later cells carry no text of their own. */
  position: relative;
  z-index: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex-shrink: 0;
}

.calendar-cell--today {
  background-color: rgba(var(--v-theme-primary), 0.05);
}

.calendar-cell--clickable {
  cursor: pointer;
}

.calendar-cell--clickable:hover {
  background-color: rgba(var(--v-theme-primary), 0.04);
}

.calendar-day-number {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
  min-width: 20px;
  height: 20px;
  margin-bottom: 2px;
}

.calendar-events {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

/* ── Base chip ─────────────────────────────────────────────────────────── */
.calendar-event-chip {
  display: flex;
  align-items: center;
  font-size: 0.7rem;
  line-height: 1.3;
  padding: 1px 5px;
  border-radius: 4px;
  cursor: pointer;
  overflow: hidden;
  min-height: 18px;
}

.calendar-event-chip:hover {
  filter: brightness(0.88);
}

.event-chip-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── Color variants ────────────────────────────────────────────────────── */
.event-chip--color-primary {
  background-color: rgb(var(--v-theme-primary));
  color: rgb(var(--v-theme-on-primary));
}
.event-chip--color-secondary {
  background-color: rgb(var(--v-theme-secondary));
  color: rgb(var(--v-theme-on-secondary));
}

/* ── All-day span styling ──────────────────────────────────────────────── */
.event-chip--all-day {
  border-radius: 4px;
  margin: 0 0px;
}
/* Continues to the right: remove right border-radius + extend to cell edge */
.event-chip--all-day.event-chip--start {
  border-top-right-radius: 0;
  border-bottom-right-radius: 0;
  margin-right: -4px;
  padding-right: 2px;
}
/* Continues from the left: remove left border-radius + extend from cell edge */
.event-chip--all-day.event-chip--end {
  border-top-left-radius: 0;
  border-bottom-left-radius: 0;
  margin-left: -4px;
  padding-left: 2px;
}
/* Middle: no border-radius, extend on both sides */
.event-chip--all-day.event-chip--middle {
  border-radius: 0;
  margin-left: -4px;
  margin-right: -4px;
  padding-left: 2px;
  padding-right: 2px;
}
</style>
