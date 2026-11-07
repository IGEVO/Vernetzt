<script setup lang="ts">
import type { PublicEvent } from '~/types/public'

const props = defineProps<{ event: PublicEvent }>()
const href = computed(() => entityPath('veranstaltungen', props.event.id ?? '', props.event.title ?? 'Veranstaltung'))
const parts = computed(() => dateBlockParts(props.event.startUtc))
const thumb = computed(() => smallestVariant(props.event.cardImage ?? props.event.image, 160))
const meta = computed(() => [
  formatEventTime(props.event.startUtc, props.event.endUtc, props.event.isAllDay),
  eventOrganizerPath(props.event),
  props.event.location
].filter(Boolean).join(', '))
</script>

<template>
  <li>
    <NuxtLink :to="href" class="row">
      <span class="date-block" aria-hidden="true">
        <span class="day">{{ parts.day }}</span>
        <span class="month">{{ parts.month }}</span>
      </span>
      <span class="row-body">
        <span class="row-title">
          <span class="visually-hidden">{{ formatEventDate(event.startUtc, event.endUtc, event.isAllDay) }}: </span>{{ event.title }}
          <span v-if="event.isHighlight" class="badge">Highlight</span>
        </span>
        <span class="row-meta">{{ meta }}</span>
      </span>
      <img v-if="thumb" :src="thumb" alt="" class="event-thumb" loading="lazy" width="92" height="62">
    </NuxtLink>
  </li>
</template>
