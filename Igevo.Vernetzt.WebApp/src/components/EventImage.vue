<script setup lang="ts">
import { computed } from 'vue'
import { hasPreview, mediaAspect, objectPosition, srcset, variantUrl } from '@/services/eventMedia'
import type { EventMediaDto } from '@/types/api'

/**
 * Displays an event image in any aspect ratio without a fixed crop:
 * - `cover`: fills the box, the focal point stays visible
 * - `auto`: like cover, but if the image orientation differs strongly from the box
 *   (e.g. A4 poster in a wide header) it is shown completely on a blurred backdrop
 */
const props = withDefaults(defineProps<{
  media?: EventMediaDto | null
  /** Width / height of the box */
  aspect?: number
  /** `sizes` attribute for responsive loading, e.g. "56px" or "(max-width: 700px) 100vw, 700px" */
  sizes?: string
  fit?: 'cover' | 'auto'
  alt?: string
  eager?: boolean
}>(), {
  media: null,
  aspect: 16 / 9,
  sizes: '100vw',
  fit: 'cover',
  alt: '',
  eager: false
})

const position = computed(() => objectPosition(props.media?.focusX, props.media?.focusY))

const contain = computed(() => {
  if (props.fit !== 'auto' || !props.media) return false
  const imageAspect = mediaAspect(props.media)
  if (!imageAspect) return false
  const ratio = imageAspect > props.aspect ? imageAspect / props.aspect : props.aspect / imageAspect
  return ratio > 1.6
})

const backdropUrl = computed(() => (props.media ? variantUrl(props.media, 160) : ''))
</script>

<template>
  <div class="event-image" :style="{ aspectRatio: String(aspect) }">
    <template v-if="hasPreview(media)">
      <img
        v-if="contain"
        :src="backdropUrl"
        alt=""
        aria-hidden="true"
        class="event-image__backdrop"
        loading="lazy"
        decoding="async"
      >
      <img
        :srcset="srcset(media)"
        :sizes="sizes"
        :src="variantUrl(media, 480)"
        :alt="alt"
        :loading="eager ? 'eager' : 'lazy'"
        decoding="async"
        class="event-image__img"
        :style="{ objectFit: contain ? 'contain' : 'cover', objectPosition: position }"
      >
    </template>
    <slot v-else />
  </div>
</template>

<style scoped>
.event-image {
  position: relative;
  width: 100%;
  overflow: hidden;
  background: rgba(var(--v-theme-on-surface), 0.06);
}
.event-image__img,
.event-image__backdrop {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
}
.event-image__backdrop {
  object-fit: cover;
  filter: blur(24px) saturate(1.2);
  transform: scale(1.2);
  opacity: 0.75;
}
</style>
