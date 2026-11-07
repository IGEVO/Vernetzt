<script setup lang="ts">
import { detectSocialMediaPlatform, socialMediaPlatform, socialMediaPlatforms } from '@/services/socialMedia'
import type { SocialMediaLinkRow, SocialMediaPlatform } from '@/types/api'

/** Editable list of social media links (platform + profile URL) for clubs and departments. */
const rows = defineModel<SocialMediaLinkRow[]>({ required: true })
const maxLinks = 10
// Bound instead of a static attribute: vue-tsc mis-parses a literal "https://" inside v-for as a line comment.
const urlPlaceholder = 'https://'
let nextKey = Date.now()

const rules = {
  platform: (value: SocialMediaPlatform | null) => value !== null || 'Plattform wählen',
  url: (value: string) => /^https?:\/\/\S+\.\S+/.test(value?.trim() ?? '') || 'Bitte vollständige URL mit https:// angeben'
}

const platformIcon = (value: SocialMediaPlatform | null): string | undefined => {
  const info = socialMediaPlatform(value)
  return info ? `svg:${info.path}` : undefined
}
const platformItemProps = (item: { value: SocialMediaPlatform }) => ({ prependIcon: platformIcon(item.value) })
const removeLabel = (value: SocialMediaPlatform | null) => `${socialMediaPlatform(value)?.label ?? 'Social-Media'}-Link entfernen`

function add(): void {
  rows.value = [...rows.value, { key: nextKey++, platform: null, url: '' }]
}

function remove(key: number): void {
  rows.value = rows.value.filter(row => row.key !== key)
}

function setPlatform(row: SocialMediaLinkRow, value: SocialMediaPlatform | null): void {
  row.platform = value
}

/** Picks the platform from a pasted profile URL, unless the user already chose one. */
function setUrl(row: SocialMediaLinkRow, value: string): void {
  row.url = value
  const detected = detectSocialMediaPlatform(value)
  if (detected && row.platform === null) row.platform = detected.value
}
</script>

<template>
  <fieldset class="social-links">
    <legend class="text-subtitle-2 mb-2">Social Media</legend>
    <div v-for="row in rows" :key="row.key" class="social-link-row">
      <div class="platform">
        <v-select
          :model-value="row.platform" label="Plattform" :items="socialMediaPlatforms" item-title="label" item-value="value"
          :item-props="platformItemProps" :prepend-inner-icon="platformIcon(row.platform)" :rules="[rules.platform]"
          @update:model-value="setPlatform(row, $event)"
        />
      </div>
      <div class="url">
        <v-text-field
          :model-value="row.url" label="Profil-URL" :placeholder="urlPlaceholder" maxlength="500" :rules="[rules.url]"
          @update:model-value="setUrl(row, $event)"
        />
      </div>
      <v-btn class="remove" icon="mdi-close" variant="text" size="small" :aria-label="removeLabel(row.platform)" @click="remove(row.key)" />
    </div>
    <v-btn v-if="rows.length < maxLinks" variant="tonal" size="small" prepend-icon="mdi-plus" class="mb-2" @click="add">Social-Media-Link hinzufügen</v-btn>
  </fieldset>
</template>

<style scoped>
.social-links { border: 0; margin: 0 0 8px; padding: 0; min-width: 0; }
.social-link-row { display: grid; grid-template-columns: 1fr auto; gap: 0 8px; }
.social-link-row .platform { grid-column: 1 / -1; }
.social-link-row .remove { margin-top: 10px; }
@media (min-width: 600px) {
  .social-link-row { grid-template-columns: minmax(150px, 1fr) 2fr auto; }
  .social-link-row .platform { grid-column: auto; }
}
</style>
