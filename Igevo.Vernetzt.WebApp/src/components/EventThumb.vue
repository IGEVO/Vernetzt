<script setup lang="ts">
import { computed } from 'vue'
import EventImage from '@/components/EventImage.vue'
import type { EventDto } from '@/types/api'

/** Square list thumbnail: event image with date badge, or a date tile as placeholder. */
const props = withDefaults(defineProps<{
  event: EventDto
  size?: number
}>(), {
  size: 56
})

const image = computed(() => props.event.listImage ?? props.event.image ?? null)
const start = computed(() => new Date(props.event.startUtc))
const day = computed(() => start.value.getDate())
const month = computed(() =>
  new Intl.DateTimeFormat('de-DE', { month: 'short' }).format(start.value).replace('.', '')
)
</script>

<template>
  <div class="event-thumb" :style="{ width: `${size}px`, height: `${size}px` }">
    <EventImage v-if="image" :media="image" :aspect="1" :sizes="`${size}px`" />
    <div v-if="image" class="event-thumb__badge">{{ day }}. {{ month }}</div>
    <div
      v-else
      class="event-thumb__tile"
      :class="event.isHighlight ? 'bg-secondary' : 'bg-primary'"
    >
      <span class="event-thumb__day">{{ day }}</span>
      <span class="event-thumb__month">{{ month }}</span>
    </div>
    <v-icon v-if="event.isHighlight" icon="mdi-star" :color="image ? 'secondary' : 'white'" size="14" class="event-thumb__star" />
  </div>
</template>

<style scoped>
.event-thumb {
  position: relative;
  flex: none;
  border-radius: 8px;
  overflow: hidden;
}
.event-thumb__tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 100%;
  line-height: 1;
}
.event-thumb__day {
  font-size: 1.35rem;
  font-weight: 700;
}
.event-thumb__month {
  font-size: 0.7rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  margin-top: 2px;
}
.event-thumb__badge {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  padding: 1px 0;
  font-size: 0.65rem;
  font-weight: 600;
  text-align: center;
  color: #fff;
  background: rgba(0, 0, 0, 0.55);
}
.event-thumb__star {
  position: absolute;
  top: 2px;
  right: 2px;
  filter: drop-shadow(0 0 2px rgba(0, 0, 0, 0.6));
}
</style>
