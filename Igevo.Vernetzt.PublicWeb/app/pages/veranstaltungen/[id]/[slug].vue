<script setup lang="ts">
import type { PublicEvent, PublicEventImage } from '~/types/public'

const ATTACHMENT = 2
const config = useRuntimeConfig()
const { id, data: event } = await useDetailData<PublicEvent>('veranstaltungen', 'events', 'Termin nicht gefunden')

const canonical = useCanonical(computed(() => entityPath('veranstaltungen', id, event.value.title ?? '')))
const hero = computed(() => event.value.headerImage ?? event.value.image)
const organizer = computed(() => {
  if (event.value.departmentId && event.value.departmentName) {
    // Full path so a group ("Bambini") is never shown without its department and club
    return { name: eventOrganizerPath(event.value), to: entityPath('abteilungen', event.value.departmentId, event.value.departmentName) }
  }
  if (event.value.clubId && event.value.clubName) {
    return { name: event.value.clubName, to: entityPath('vereine', event.value.clubId, event.value.clubName) }
  }
  return null
})
const gallery = computed(() => (event.value.media ?? [])
  .filter(item => item.type !== ATTACHMENT && item.variants?.length && item.id !== hero.value?.id))
const attachments = computed(() => (event.value.media ?? []).filter(item => item.type === ATTACHMENT && item.fileUrl))
const attachmentLabel = (item: PublicEventImage, index: number) => {
  const kind = item.contentType === 'application/pdf' ? 'PDF' : 'Datei'
  return attachments.value.length > 1 ? `${kind} ${index + 1} herunterladen` : `${kind} herunterladen`
}
const absolute = (url?: string | null) => url ? new URL(url, config.public.siteUrl).toString() : undefined

useSeoMeta({
  title: () => event.value.title ?? 'Termin',
  description: () => event.value.description?.slice(0, 155)
    || `${event.value.title}, ${formatEventDate(event.value.startUtc, event.value.endUtc, event.value.isAllDay)}`,
  ogTitle: () => event.value.title,
  ogDescription: () => event.value.description?.slice(0, 200),
  ogUrl: canonical,
  ogImage: () => absolute(smallestVariant(hero.value, 1200))
})
useHead(() => ({
  script: [{
    type: 'application/ld+json',
    innerHTML: JSON.stringify({
      '@context': 'https://schema.org', '@type': 'Event', name: event.value.title,
      startDate: event.value.startUtc, endDate: event.value.endUtc,
      eventAttendanceMode: 'https://schema.org/OfflineEventAttendanceMode',
      eventStatus: 'https://schema.org/EventScheduled',
      location: event.value.location ? { '@type': 'Place', name: event.value.location } : undefined,
      organizer: organizer.value ? { '@type': 'Organization', name: organizer.value.name, url: absolute(organizer.value.to) } : undefined,
      image: hero.value?.variants?.map(item => absolute(item.url)),
      description: event.value.description ?? undefined,
      url: canonical.value
    })
  }]
}))
</script>

<template>
  <div class="container narrow page">
    <NuxtLink to="/veranstaltungen" class="back-link"><AppIcon name="back" />Alle Termine</NuxtLink>

    <img
      v-if="hero?.variants?.length"
      :src="smallestVariant(hero, 960)" :srcset="srcsetOf(hero)" sizes="(max-width: 760px) 100vw, 712px"
      :width="hero.width ?? undefined" :height="hero.height ?? undefined"
      alt="" class="event-hero" :style="{ objectPosition: focusPosition(hero) }"
    >

    <div class="detail-title">
      <h1>{{ event.title }}<span v-if="event.isHighlight" class="badge">Highlight</span></h1>
    </div>

    <dl class="facts">
      <div>
        <AppIcon name="clock" /><dt>Wann</dt>
        <dd>{{ formatEventDate(event.startUtc, event.endUtc, event.isAllDay) }}</dd>
      </div>
      <div v-if="event.location">
        <AppIcon name="pin" /><dt>Ort</dt>
        <dd>{{ event.location }}</dd>
      </div>
      <div v-if="organizer">
        <AppIcon name="users" /><dt>Veranstalter</dt>
        <dd><NuxtLink :to="organizer.to">{{ organizer.name }}</NuxtLink></dd>
      </div>
    </dl>

    <p v-if="event.description" class="prose" style="margin-top:1.75rem">{{ event.description }}</p>

    <section v-if="attachments.length" class="section" style="margin-top:2rem" aria-labelledby="downloads-heading">
      <h2 id="downloads-heading" style="font-size:1.1rem">Unterlagen</h2>
      <ul class="downloads">
        <li v-for="(item, index) in attachments" :key="item.id">
          <a :href="item.fileUrl!" download><AppIcon name="download" />{{ attachmentLabel(item, index) }}</a>
        </li>
      </ul>
    </section>

    <section v-if="gallery.length" class="section" style="margin-top:2rem" aria-labelledby="gallery-heading">
      <h2 id="gallery-heading" style="font-size:1.1rem">Bilder</h2>
      <div class="gallery">
        <a v-for="item in gallery" :key="item.id" :href="smallestVariant(item, 1600)" target="_blank" rel="noopener">
          <img
            :src="smallestVariant(item, 320)" alt="" loading="lazy" width="320" height="320"
            :style="{ objectPosition: focusPosition(item) }"
          >
          <span class="visually-hidden">Bild in voller Größe öffnen</span>
        </a>
      </div>
    </section>
  </div>
</template>
