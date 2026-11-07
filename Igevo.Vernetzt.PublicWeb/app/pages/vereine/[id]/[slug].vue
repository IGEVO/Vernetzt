<script setup lang="ts">
import type { PublicClub, PublicEventPage } from '~/types/public'

const config = useRuntimeConfig()
const { id, data: club } = await useDetailData<PublicClub>('vereine', 'clubs', 'Verein nicht gefunden')

const { data: events } = await useFetch<PublicEventPage>('/api/public/v1/events', {
  key: `club-events:${id}`,
  query: { clubId: id, fromUtc: berlinDayBoundaryUtc(todayIsoDate()), pageSize: 5 }
})

const canonical = useCanonical(computed(() => entityPath('vereine', id, club.value.name ?? '')))
const addressLines = computed(() => [club.value.street, [club.value.postalCode, club.value.city].filter(Boolean).join(' ')].filter(Boolean))
const website = computed(() => safeExternalUrl(club.value.websiteUrl))
const sameAs = computed(() => [website.value, ...(club.value.socialMediaLinks ?? []).map(link => safeExternalUrl(link.url))]
  .filter((url): url is string => !!url))

useSeoMeta({
  title: () => club.value.name ?? 'Verein',
  description: () => `${club.value.name}${club.value.city ? ` in ${club.value.city}` : ''}: Ansprechpersonen, Abteilungen und Termine.`,
  ogTitle: () => club.value.name,
  ogUrl: canonical,
  ogImage: () => club.value.logoUrl ? new URL(club.value.logoUrl, config.public.siteUrl).toString() : undefined
})
useHead(() => ({
  script: [{
    type: 'application/ld+json',
    innerHTML: JSON.stringify({
      '@context': 'https://schema.org', '@type': 'Organization',
      name: club.value.name, url: canonical.value, description: club.value.description ?? undefined,
      sameAs: sameAs.value.length ? sameAs.value : undefined,
      logo: club.value.logoUrl ? new URL(club.value.logoUrl, config.public.siteUrl).toString() : undefined,
      address: addressLines.value.length
        ? { '@type': 'PostalAddress', streetAddress: club.value.street, postalCode: club.value.postalCode, addressLocality: club.value.city, addressCountry: 'DE' }
        : undefined
    })
  }]
}))
</script>

<template>
  <div class="container page">
    <NuxtLink to="/vereine" class="back-link"><AppIcon name="back" />Alle Vereine</NuxtLink>

    <div class="detail-head">
      <span class="logo-box large" :class="{ initial: !club.logoUrl }" aria-hidden="true">
        <img v-if="club.logoUrl" :src="club.logoUrl" alt="" width="96" height="96">
        <template v-else>{{ club.name?.charAt(0) }}</template>
      </span>
      <div>
        <p v-if="club.clubTypeName" class="kicker">{{ club.clubTypeName }}</p>
        <h1>{{ club.name }}</h1>
      </div>
    </div>

    <p v-if="club.description" class="prose description" style="margin-top:1.5rem">{{ club.description }}</p>

    <dl v-if="addressLines.length || club.hasEmail || website || club.socialMediaLinks?.length" class="facts">
      <div v-if="addressLines.length">
        <AppIcon name="pin" /><dt>Adresse</dt>
        <dd>{{ addressLines.join(', ') }}</dd>
      </div>
      <div v-if="club.hasEmail">
        <AppIcon name="mail" /><dt>E-Mail</dt>
        <dd><ContactReveal :source="`/api/public/v1/clubs/${id}/contact-details`" has-email :owner="club.name ?? ''" /></dd>
      </div>
      <div v-if="website">
        <AppIcon name="globe" /><dt>Website</dt>
        <dd><a :href="website" rel="noopener noreferrer" target="_blank">{{ displayHost(website) }}</a></dd>
      </div>
      <div v-if="club.socialMediaLinks?.length">
        <AppIcon name="share" /><dt>Social Media</dt>
        <dd><SocialLinks :links="club.socialMediaLinks" :name="club.name" /></dd>
      </div>
    </dl>

    <section class="section" style="margin-top:2.5rem" aria-labelledby="contacts-heading">
      <div class="section-head"><h2 id="contacts-heading">Ansprechpersonen</h2></div>
      <ContactList :contacts="club.contacts" />
    </section>

    <section v-if="club.departments?.length" class="section" aria-labelledby="departments-heading">
      <div class="section-head"><h2 id="departments-heading">Abteilungen</h2></div>
      <ul class="list two-col">
        <li v-for="department in club.departments" :key="department.id">
          <NuxtLink :to="entityPath('abteilungen', department.id || '', department.name || '')" class="row">
            <span class="logo-box" :class="{ initial: !department.logoUrl }" aria-hidden="true">
              <img v-if="department.logoUrl" :src="department.logoUrl" alt="" loading="lazy" width="52" height="52">
              <template v-else>{{ department.name?.charAt(0) }}</template>
            </span>
            <span class="row-body">
              <span class="row-title">{{ department.name }}</span>
              <span v-if="department.groupCount || firstLine(department.description)" class="row-meta">
                {{ [department.groupCount ? `${department.groupCount} ${department.groupCount === 1 ? 'Gruppe' : 'Gruppen'}` : null, firstLine(department.description)].filter(Boolean).join(' · ') }}
              </span>
            </span>
          </NuxtLink>
        </li>
      </ul>
    </section>

    <section v-if="events?.items?.length" class="section" aria-labelledby="club-events-heading">
      <div class="section-head">
        <h2 id="club-events-heading">Nächste Termine</h2>
        <NuxtLink v-if="(events.totalCount ?? 0) > events.items.length" :to="{ path: '/veranstaltungen', query: { clubId: id } }">
          Alle {{ events.totalCount }} Termine
        </NuxtLink>
      </div>
      <ul class="list">
        <EventRow v-for="item in events.items" :key="item.id" :event="item" />
      </ul>
    </section>
  </div>
</template>
