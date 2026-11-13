<script setup lang="ts">
import type { PublicClubPage, PublicClubType, PublicEventPage } from '~/types/public'

const apiFetch = useRequestFetch()
const fromUtc = berlinDayBoundaryUtc(todayIsoDate())

const { data, error } = await useAsyncData('home', async () => {
  const [clubs, events, clubTypes] = await Promise.all([
    apiFetch<PublicClubPage>('/api/public/v1/clubs/random', { query: { count: 8 } }),
    apiFetch<PublicEventPage>('/api/public/v1/events', { query: { fromUtc, pageSize: 6 } }),
    apiFetch<readonly PublicClubType[]>('/api/public/v1/club-types')
  ])
  return { clubs, events, clubTypes }
})

const { data: portal } = await usePortal()
const heading = computed(() => portal.value?.title || 'Vereine und Termine')
const intro = computed(() => portal.value?.subtitle || 'Kontakte, Abteilungen und öffentliche Termine, gepflegt von den Vereinen selbst.')

useCanonical('/')
useSeoMeta({
  title: heading,
  description: intro,
  ogTitle: heading
})
</script>

<template>
  <div>
    <section class="home-band">
      <div class="container">
        <img src="/logo.svg" alt="" class="home-graphic">
        <div class="home-intro" :class="{ 'has-logo': portal?.logoUrl }">
          <img v-if="portal?.logoUrl" :src="portal.logoUrl" :alt="portal.provider?.name ? `Logo ${portal.provider.name}` : 'Logo'" class="home-logo">
          <div>
            <h1>{{ heading }}</h1>
            <p class="intro">{{ intro }}</p>
            <p v-if="portal?.provider" class="home-provider">
              Ein Angebot von
              <a v-if="portal.provider.url" :href="portal.provider.url" rel="noopener">{{ portal.provider.name }}</a>
              <template v-else>{{ portal.provider.name }}</template>
            </p>
          </div>
        </div>

        <form action="/vereine" method="get" role="search" class="search">
          <div class="search-field">
            <label for="home-search" class="visually-hidden">Verein oder Abteilung suchen</label>
            <AppIcon name="search" />
            <input id="home-search" name="query" type="search" placeholder="Verein oder Abteilung" autocomplete="off" enterkeyhint="search">
          </div>
          <button type="submit" class="btn btn-amber">Suchen</button>
        </form>

        <ul v-if="data?.clubTypes?.length" class="type-links" aria-label="Vereine nach Art">
          <li v-for="type in data.clubTypes" :key="type.id">
            <NuxtLink :to="{ path: '/vereine', query: { clubTypeId: type.id } }">{{ type.name }}</NuxtLink>
          </li>
        </ul>
      </div>
    </section>

    <div class="container page">
      <p v-if="error" class="notice error" role="alert">
        Die Daten können gerade nicht geladen werden. Bitte versuche es in ein paar Minuten noch einmal.
      </p>

      <template v-else>
        <section class="section" aria-labelledby="events-heading">
          <div class="section-head">
            <h2 id="events-heading">Nächste Termine</h2>
            <NuxtLink to="/veranstaltungen">Alle Termine</NuxtLink>
          </div>
          <ul v-if="data?.events.items?.length" class="list">
            <EventRow v-for="item in data.events.items" :key="item.id" :event="item" />
          </ul>
          <p v-else class="empty">Zurzeit sind keine öffentlichen Termine eingetragen.</p>
        </section>

        <section class="section" aria-labelledby="clubs-heading">
          <div class="section-head">
            <h2 id="clubs-heading">Vereine</h2>
            <NuxtLink to="/vereine">Alle {{ data?.clubs.totalCount || '' }} Vereine</NuxtLink>
          </div>
          <ul v-if="data?.clubs.items?.length" class="list two-col">
            <ClubRow v-for="club in data.clubs.items" :key="club.id" :club="club" />
          </ul>
          <p v-else class="empty">Noch keine Vereine eingetragen.</p>
        </section>
      </template>
    </div>
  </div>
</template>
