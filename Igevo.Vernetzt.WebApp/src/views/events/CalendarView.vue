<script setup lang="ts">
import { eventOrganizer } from '@/services/organizationPath'
import { computed, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import CalendarFeedLink from '@/components/CalendarFeedLink.vue'
import EventCalendarGrid from '@/components/EventCalendarGrid.vue'
import EventDetailDialog from '@/components/EventDetailDialog.vue'
import EventThumb from '@/components/EventThumb.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useCalendarHolidays } from '@/composables/useCalendarHolidays'
import { api } from '@/services/api'
import type { CalendarFeedTokenDto, EventDto } from '@/types/api'

const highlightsOnly = ref(false)
const mainTab = ref<'calendar' | 'list' | 'ical'>('calendar')
const showPast = ref(false)
const selected = ref<EventDto | null>(null)
const visibleMonth = ref({ year: new Date().getFullYear(), month: new Date().getMonth() + 1 })
const { holidays, onViewMonthChange: onHolidayMonthChange } = useCalendarHolidays()

const events = useQuery({
  queryKey: ['events', 'global'],
  queryFn: ({ signal }) =>
    api.get<EventDto[]>('/api/events/public/global?highlightsOnly=false', signal)
})

// Global subscription links: the API only returns active ones to regular users; show nothing if none exist.
const feeds = useQuery({
  queryKey: ['calendar-feeds', 'global'],
  queryFn: ({ signal }) =>
    api.get<CalendarFeedTokenDto[]>('/api/calendar-feeds/tokens?globalOnly=true', signal)
})
const globalFeeds = computed(() =>
  [
    { title: 'Alle Termine', subtitle: 'Termine aller Vereine und Abteilungen', highlights: false },
    { title: 'Nur Highlights', subtitle: 'Nur hervorgehobene Termine', highlights: true }
  ]
    .map((item) => ({
      ...item,
      token: (feeds.data.value ?? []).find((t) => !t.isRevoked && !!t.feedUrl && t.highlightsOnly === item.highlights)
    }))
    .filter((item) => item.token)
)

const filtered = computed(() =>
  (events.data.value ?? []).filter((item) => !highlightsOnly.value || item.isHighlight)
)

const now = new Date()

const upcoming = computed(() =>
  filtered.value.filter((item) => new Date(item.endUtc) >= now).sort((a, b) =>
    a.startUtc.localeCompare(b.startUtc)
  )
)

// Same behaviour as the club / department list: past events are included chronologically on demand.
const visible = computed(() =>
  showPast.value ? [...filtered.value].sort((a, b) => a.startUtc.localeCompare(b.startUtc)) : upcoming.value
)

function overlapsVisibleMonth(item: EventDto): boolean {
  const monthStart = new Date(visibleMonth.value.year, visibleMonth.value.month - 1, 1)
  const nextMonthStart = new Date(visibleMonth.value.year, visibleMonth.value.month, 1)
  const start = new Date(item.startUtc)
  const end = new Date(item.endUtc)
  return start < nextMonthStart && end > monthStart
}

const monthEventsCount = computed(() => filtered.value.filter(overlapsVisibleMonth).length)

const chipLabel = computed(() =>
  mainTab.value === 'calendar'
    ? `${monthEventsCount.value} im angezeigten Monat`
    : `${upcoming.value.length} anstehend`
)

function onViewMonthChange(payload: { year: number; month: number }): void {
  visibleMonth.value = payload
  onHolidayMonthChange(payload)
}

const sourceLabel = (item: EventDto): string => eventOrganizer(item)

const dateFormatter = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' })
const dateOnlyFormatter = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })

function formatEvent(item: EventDto): string {
  if (item.isAllDay) {
    return dateOnlyFormatter.format(new Date(item.startUtc))
  }
  return `${dateFormatter.format(new Date(item.startUtc))} – ${dateFormatter.format(new Date(item.endUtc))}`
}
</script>

<template>
  <PageHeader title="Kalender" />

  <!-- Same structure as the club / department calendar (EntityEventsView). -->
  <v-tabs v-model="mainTab" color="primary" class="mb-4">
    <v-tab value="calendar" prepend-icon="mdi-calendar-month">Kalender</v-tab>
    <v-tab value="list" prepend-icon="mdi-format-list-bulleted">Liste</v-tab>
    <v-tab v-if="globalFeeds.length" value="ical" prepend-icon="mdi-calendar-sync">iCal-Abos</v-tab>
  </v-tabs>

  <template v-if="mainTab === 'calendar' || mainTab === 'list'">
    <div class="d-flex align-center flex-wrap ga-3 mb-3">
      <v-chip :color="highlightsOnly ? 'secondary' : 'default'" variant="tonal">
        <v-icon start icon="mdi-information-outline" />
        {{ chipLabel }}
      </v-chip>
      <v-spacer />
      <v-switch
        v-if="mainTab === 'list'"
        v-model="showPast"
        label="Vergangene anzeigen"
        color="secondary"
        hide-details
        density="compact"
        class="flex-grow-0"
      />
      <v-switch
        v-model="highlightsOnly"
        label="Nur Highlights"
        color="secondary"
        hide-details
        density="compact"
        class="flex-grow-0"
      />
    </div>

    <v-progress-linear v-if="events.isPending.value" indeterminate />
    <v-alert v-else-if="events.isError.value" type="error">
      Termine konnten nicht geladen werden.
    </v-alert>

    <!-- ═══ CALENDAR TAB ══════════════════════════════════════════════════ -->
    <template v-else-if="mainTab === 'calendar'">
      <v-empty-state
        v-if="!filtered.length && !holidays.length"
        title="Keine Termine"
        icon="mdi-calendar-blank"
      />
      <EventCalendarGrid
        v-else
        :events="filtered"
        :holidays="holidays"
        @click-event="selected = $event"
        @view-month-change="onViewMonthChange"
      />
    </template>

    <!-- ═══ LIST TAB ══════════════════════════════════════════════════════ -->
    <template v-else>
      <v-empty-state
        v-if="!visible.length"
        :title="showPast ? 'Keine Termine' : 'Keine anstehenden Termine'"
        icon="mdi-calendar-blank"
      />
      <v-list v-else lines="two" class="py-0">
        <v-list-item
          v-for="item in visible"
          :key="item.id"
          class="border rounded mb-2 cursor-pointer mx-1"
          @click="selected = item"
        >
          <template #prepend>
            <EventThumb :event="item" class="mr-4" />
          </template>
          <v-list-item-title class="font-weight-medium">{{ item.title }}</v-list-item-title>
          <v-list-item-subtitle>
            {{ formatEvent(item) }}
            <template v-if="item.location"> · <v-icon icon="mdi-map-marker" size="12" />{{ item.location }}</template>
            <v-chip
              v-if="item.clubName || item.departmentName"
              size="x-small"
              color="primary"
              variant="tonal"
              class="ml-2"
            >
              {{ sourceLabel(item) }}
            </v-chip>
          </v-list-item-subtitle>
        </v-list-item>
      </v-list>
    </template>
  </template>

  <!-- ═══ iCAL TAB ════════════════════════════════════════════════════════ -->
  <v-card v-else-if="mainTab === 'ical' && globalFeeds.length" variant="outlined" class="mt-2">
    <v-card-item title="Kalender abonnieren (iCal)" prepend-icon="mdi-calendar-sync" />
    <v-card-text>
      <p class="text-body-medium text-medium-emphasis mb-4">
        Mit diesen Links kannst du den Kalender in Outlook, Apple Kalender, Google Kalender oder
        anderen Anwendungen abonnieren. Kopiere den Link und füge ihn dort als „Kalender-Abonnement“ ein.
      </p>
      <template v-for="(item, index) in globalFeeds" :key="item.title">
        <v-divider v-if="index > 0" class="my-4" />
        <CalendarFeedLink :title="item.title" :subtitle="item.subtitle" :token="item.token" readonly />
      </template>
    </v-card-text>
  </v-card>

  <EventDetailDialog
    :model-value="!!selected"
    :event="selected"
    @update:model-value="!$event && (selected = null)"
  />
</template>
