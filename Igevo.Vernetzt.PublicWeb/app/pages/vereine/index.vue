<script setup lang="ts">
import type { PublicClubPage, PublicClubType } from '~/types/public'

const route = useRoute()
const apiFetch = useRequestFetch()
const text = (value: unknown) => typeof value === 'string' ? value.trim().slice(0, 100) : ''
const query = computed(() => text(route.query.query))
const clubTypeId = computed(() => isGuid(route.query.clubTypeId) ? route.query.clubTypeId : '')
const page = computed(() => Math.min(10_000, Math.max(1, Math.trunc(Number(route.query.page)) || 1)))

const { data: clubTypes } = await useFetch<readonly PublicClubType[]>('/api/public/v1/club-types', { default: () => [] })
const { data, error, status } = await useAsyncData(
  () => `clubs:${query.value}:${clubTypeId.value}:${page.value}`,
  () => apiFetch<PublicClubPage>('/api/public/v1/clubs', {
    query: {
      query: query.value || undefined,
      clubTypeId: clubTypeId.value || undefined,
      page: page.value,
      pageSize: 24
    }
  })
)

const isFiltered = computed(() => Boolean(query.value || clubTypeId.value))
const resultText = computed(() => {
  const total = data.value?.totalCount ?? 0
  if (!isFiltered.value) return total === 1 ? '1 Verein' : `${total} Vereine`
  return total === 0 ? 'Keine Treffer' : total === 1 ? '1 Treffer' : `${total} Treffer`
})

// Search results are not indexed; only the plain (paginated) directory is canonical.
useCanonical(computed(() => page.value > 1 ? `/vereine?page=${page.value}` : '/vereine'))
useSeoMeta({
  title: 'Vereine',
  description: 'Alle Vereine mit Abteilungen, Ansprechpersonen und Kontaktdaten.',
  robots: () => isFiltered.value ? 'noindex, follow' : 'index, follow'
})
</script>

<template>
  <div class="container page">
    <div class="page-title">
      <h1>Vereine</h1>
    </div>

    <form action="/vereine" method="get" role="search" class="filters filters-short" aria-label="Vereine durchsuchen">
      <div class="field">
        <label for="club-query">Name oder Abteilung</label>
        <input id="club-query" name="query" type="search" :value="query" maxlength="100" enterkeyhint="search">
      </div>
      <div class="field">
        <label for="club-type">Art</label>
        <select id="club-type" name="clubTypeId">
          <option value="">Alle</option>
          <option v-for="type in clubTypes" :key="type.id" :value="type.id" :selected="type.id === clubTypeId">{{ type.name }}</option>
        </select>
      </div>
      <button type="submit" class="btn btn-primary">Suchen</button>
    </form>

    <p v-if="error" class="notice error" role="alert">
      Die Vereine können gerade nicht geladen werden. Bitte versuche es in ein paar Minuten noch einmal.
    </p>
    <template v-else>
      <p class="result-count" aria-live="polite">
        {{ status === 'pending' ? 'Wird geladen …' : resultText }}
        <NuxtLink v-if="isFiltered" to="/vereine" style="margin-left:.5rem">Filter zurücksetzen</NuxtLink>
      </p>
      <ul v-if="data?.items?.length" class="list two-col">
        <ClubRow v-for="club in data.items" :key="club.id" :club="club" />
      </ul>
      <p v-else-if="status !== 'pending'" class="empty">
        Kein Verein passt zu dieser Suche. Versuche einen kürzeren Begriff oder eine andere Art.
      </p>
      <PaginationNav :page="data?.page || page" :total-pages="data?.totalPages || 1" />
    </template>
  </div>
</template>
