<script setup lang="ts">
import type { PublicClubPage, PublicEvent, PublicEventPage } from '~/types/public'

const route = useRoute()
const apiFetch = useRequestFetch()
const isoDate = (value: unknown) => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : ''
const today = todayIsoDate()
const from = computed(() => isoDate(route.query.from) || today)
const to = computed(() => isoDate(route.query.to))
const clubId = computed(() => isGuid(route.query.clubId) ? route.query.clubId : '')
const page = computed(() => Math.min(10_000, Math.max(1, Math.trunc(Number(route.query.page)) || 1)))

const { data: clubs } = await useFetch<PublicClubPage>('/api/public/v1/clubs', {
  query: { pageSize: 100 },
  default: () => ({ items: [], totalCount: 0, page: 1, pageSize: 100, totalPages: 1 })
})
const { data, error, status } = await useAsyncData(
  () => `events:${from.value}:${to.value}:${clubId.value}:${page.value}`,
  () => apiFetch<PublicEventPage>('/api/public/v1/events', {
    query: {
      fromUtc: berlinDayBoundaryUtc(from.value),
      toUtc: berlinDayBoundaryUtc(to.value, true),
      clubId: clubId.value || undefined,
      page: page.value,
      pageSize: 30
    }
  })
)

const groups = computed(() => {
  const result: { label: string; items: PublicEvent[] }[] = []
  for (const item of data.value?.items ?? []) {
    const label = monthLabel(item.startUtc)
    const last = result.at(-1)
    if (last?.label === label) last.items.push(item)
    else result.push({ label, items: [item] })
  }
  return result
})

const isFiltered = computed(() => Boolean(clubId.value || to.value || from.value !== today))
const total = computed(() => data.value?.totalCount ?? 0)

useCanonical(computed(() => page.value > 1 ? `/veranstaltungen?page=${page.value}` : '/veranstaltungen'))
useSeoMeta({
  title: 'Termine',
  description: 'Öffentliche Veranstaltungen der Vereine und Abteilungen.',
  robots: () => isFiltered.value ? 'noindex, follow' : 'index, follow'
})
</script>

<template>
  <div class="container page">
    <div class="page-title">
      <h1>Termine</h1>
    </div>

    <form action="/veranstaltungen" method="get" class="filters events" aria-label="Termine filtern">
      <div class="field">
        <label for="event-club">Verein</label>
        <select id="event-club" name="clubId">
          <option value="">Alle Vereine</option>
          <option v-for="club in clubs?.items || []" :key="club.id" :value="club.id" :selected="club.id === clubId">{{ club.name }}</option>
        </select>
      </div>
      <div class="field">
        <label for="event-from">Ab</label>
        <input id="event-from" name="from" type="date" :value="from">
      </div>
      <div class="field">
        <label for="event-to">Bis</label>
        <input id="event-to" name="to" type="date" :value="to" :min="from">
      </div>
      <button type="submit" class="btn btn-primary">Anzeigen</button>
    </form>

    <p v-if="error" class="notice error" role="alert">
      Die Termine können gerade nicht geladen werden. Bitte versuche es in ein paar Minuten noch einmal.
    </p>
    <template v-else>
      <p class="result-count" aria-live="polite">
        {{ status === 'pending' ? 'Wird geladen …' : total === 1 ? '1 Termin' : `${total} Termine` }}
        <NuxtLink v-if="isFiltered" to="/veranstaltungen" style="margin-left:.5rem">Filter zurücksetzen</NuxtLink>
      </p>
      <template v-if="groups.length">
        <section v-for="group in groups" :key="group.label" class="month-group" :aria-label="group.label">
          <h2 class="month-heading">{{ group.label }}</h2>
          <ul class="list">
            <EventRow v-for="item in group.items" :key="item.id" :event="item" />
          </ul>
        </section>
      </template>
      <p v-else-if="status !== 'pending'" class="empty">
        In diesem Zeitraum gibt es keine öffentlichen Termine. Wähle einen anderen Zeitraum oder alle Vereine.
      </p>
      <PaginationNav :page="data?.page || page" :total-pages="data?.totalPages || 1" />
    </template>
  </div>
</template>
