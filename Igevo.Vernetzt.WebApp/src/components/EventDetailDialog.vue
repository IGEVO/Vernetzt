<script setup lang="ts">
import { eventOrganizer } from '@/services/organizationPath'
import { computed, ref } from 'vue'
import EventImage from '@/components/EventImage.vue'
import { detailHeader, formatFileSize, hasPreview, isImageFile, mediaIcon, mediaTypeLabel, POSTER_ASPECT, srcset, variantUrl } from '@/services/eventMedia'
import { sanitizeEmailHtml } from '@/services/html'
import type { EventDto, EventMediaDto } from '@/types/api'

const props = withDefaults(defineProps<{
  modelValue: boolean
  event: EventDto | null
  canEdit?: boolean
  showSource?: boolean
}>(), {
  canEdit: false,
  showSource: true
})

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'edit', event: EventDto): void
}>()

/** The poster is shown uncropped in its own section, not in the gallery. */
const poster = computed(() => props.event?.posterImage ?? null)

const headerImage = computed(() =>
  detailHeader(props.event?.headerImage ?? props.event?.image, poster.value)
)

/** Further images (incl. PDF pages with preview) shown as a small gallery. */
const gallery = computed(() =>
  (props.event?.media ?? []).filter((m) => hasPreview(m) && m.id !== headerImage.value?.id && m.id !== poster.value?.id)
)

/** Downloadable documents (PDFs etc.), including the one used as header if it is a PDF. */
const documents = computed(() =>
  (props.event?.media ?? []).filter((m) => !isImageFile(m))
)

const lightbox = ref<EventMediaDto | null>(null)

function openMedia(media: EventMediaDto): void {
  if (isImageFile(media)) lightbox.value = media
  else window.open(media.fileUrl, '_blank', 'noopener')
}

const dateFormatter = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' })
const dateOnlyFormatter = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })

function formatEvent(item: EventDto): string {
  if (item.isAllDay) {
    const start = dateOnlyFormatter.format(new Date(item.startUtc))
    const endExcl = new Date(item.endUtc)
    endExcl.setDate(endExcl.getDate() - 1)
    const end = dateOnlyFormatter.format(endExcl)
    return start === end ? start : `${start} – ${end}`
  }
  return `${dateFormatter.format(new Date(item.startUtc))} – ${dateFormatter.format(new Date(item.endUtc))}`
}

const sourceLabel = (item: EventDto): string => eventOrganizer(item)

</script>

<template>
  <v-dialog
    :model-value="modelValue"
    max-width="700"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <v-card v-if="event">
      <button
        v-if="headerImage"
        type="button"
        class="event-header"
        :aria-label="`${event.title}: Bild vergrößern`"
        @click="openMedia(headerImage)"
      >
        <EventImage
          :media="headerImage"
          :aspect="2"
          fit="auto"
          sizes="(max-width: 700px) 100vw, 700px"
          :alt="event.title"
          eager
        />
      </button>

      <v-card-title class="d-flex align-center gap-2 pt-4 px-6">
        <v-icon v-if="event.isHighlight" icon="mdi-star" color="secondary" />
        <span>{{ event.title }}</span>
      </v-card-title>

      <v-card-text class="px-6">
        <div class="mb-2">
          <v-icon icon="mdi-clock-outline" size="16" class="mr-1" />
          <span class="text-body-medium">{{ formatEvent(event) }}</span>
        </div>

        <div v-if="event.location" class="mb-2">
          <v-icon icon="mdi-map-marker" size="16" class="mr-1" />
          <span class="text-body-medium">{{ event.location }}</span>
        </div>

        <div v-if="showSource && (event.clubName || event.departmentName)" class="mb-2">
          <v-chip size="small" color="primary" variant="tonal">
            {{ sourceLabel(event) }}
          </v-chip>
        </div>

        <!-- eslint-disable vue/no-v-html -- description is sanitized with DOMPurify (strict tag/attribute allowlist) -->
        <div
          v-if="event.description"
          class="event-description mt-3"
          v-html="sanitizeEmailHtml(event.description)"
        />
        <!-- eslint-enable vue/no-v-html -->

        <template v-if="poster">
          <v-divider class="my-3" />
          <div class="text-title-small mb-2">Plakat</div>
          <button
            type="button"
            class="poster"
            :style="{ aspectRatio: String(POSTER_ASPECT) }"
            :aria-label="`${event.title}: Plakat vergrößern`"
            @click="openMedia(poster)"
          >
            <img
              :srcset="srcset(poster)"
              sizes="(max-width: 700px) 100vw, 420px"
              :src="variantUrl(poster, 960)"
              :alt="`Plakat: ${event.title}`"
              loading="lazy"
              decoding="async"
              class="poster__img"
            >
          </button>
        </template>

        <template v-if="gallery.length">
          <v-divider class="my-3" />
          <div class="text-title-small mb-2">Bilder</div>
          <div class="d-flex flex-wrap ga-2">
            <button
              v-for="m in gallery"
              :key="m.id"
              type="button"
              class="gallery-item"
              :aria-label="`${m.fileName} öffnen`"
              @click="openMedia(m)"
            >
              <EventImage :media="m" :aspect="1" sizes="96px" />
            </button>
          </div>
        </template>

        <template v-if="documents.length">
          <v-divider class="my-3" />
          <div class="text-title-small mb-2">Dokumente</div>
          <v-list density="compact" class="pa-0">
            <v-list-item
              v-for="m in documents"
              :key="m.id"
              :href="m.fileUrl"
              target="_blank"
              :prepend-icon="mediaIcon(m)"
              :title="m.fileName"
              :subtitle="`${mediaTypeLabel(m.type)} · ${formatFileSize(m.fileSize)}`"
            />
          </v-list>
        </template>
      </v-card-text>

      <v-card-actions class="px-6 pb-4">
        <v-spacer />
        <v-btn
          v-if="canEdit"
          variant="text"
          prepend-icon="mdi-pencil"
          @click="emit('edit', event)"
        >
          Bearbeiten
        </v-btn>
        <v-btn @click="emit('update:modelValue', false)">Schließen</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>

  <v-dialog
    :model-value="!!lightbox"
    max-width="1100"
    @update:model-value="!$event && (lightbox = null)"
  >
    <v-card v-if="lightbox" color="black" class="lightbox">
      <img
        :src="variantUrl(lightbox, 1600)"
        :alt="lightbox.fileName"
        class="lightbox__img"
      >
      <div class="lightbox__actions">
        <v-btn
          :href="lightbox.fileUrl"
          target="_blank"
          variant="tonal"
          color="white"
          size="small"
          prepend-icon="mdi-open-in-new"
        >
          Original
        </v-btn>
        <v-btn icon="mdi-close" variant="tonal" color="white" size="small" aria-label="Schließen" @click="lightbox = null" />
      </div>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.event-header {
  display: block;
  width: 100%;
  padding: 0;
  border: 0;
  cursor: zoom-in;
}
.poster {
  display: block;
  width: 100%;
  max-width: 420px;
  padding: 0;
  border: 0;
  border-radius: 8px;
  overflow: hidden;
  background: rgba(var(--v-theme-on-surface), 0.06);
  cursor: zoom-in;
}
.poster__img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: contain;
}
.gallery-item {
  width: 96px;
  padding: 0;
  border: 0;
  border-radius: 8px;
  overflow: hidden;
  cursor: zoom-in;
}
.lightbox {
  position: relative;
  display: flex;
  justify-content: center;
}
.lightbox__img {
  display: block;
  max-width: 100%;
  max-height: 85vh;
  object-fit: contain;
}
.lightbox__actions {
  position: absolute;
  top: 8px;
  right: 8px;
  display: flex;
  gap: 8px;
}
.event-description :deep(p) { margin-bottom: 0.5rem; }
.event-description :deep(ul), .event-description :deep(ol) { padding-left: 1.5rem; margin-bottom: 0.5rem; }
</style>
