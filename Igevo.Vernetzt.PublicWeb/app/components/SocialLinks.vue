<script setup lang="ts">
import type { PublicSocialMediaLink } from '~/types/public'

/** Row of brand icons linking to a club's or department's social media profiles. */
const props = defineProps<{ links?: readonly PublicSocialMediaLink[] | null; name?: string | null }>()

const items = computed(() => (props.links ?? []).flatMap((link) => {
  const url = safeExternalUrl(link.url)
  const platform = socialMediaPlatform(link.platform)
  return url && platform ? [{ url, platform }] : []
}))
</script>

<template>
  <ul v-if="items.length" class="social-links">
    <li v-for="item in items" :key="item.url">
      <a
        :href="item.url" rel="noopener noreferrer" target="_blank"
        :aria-label="`${name ? `${name} auf ` : ''}${item.platform.label}`" :title="item.platform.label"
      >
        <svg class="social-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path :d="item.platform.path" fill="currentColor" /></svg>
      </a>
    </li>
  </ul>
</template>
