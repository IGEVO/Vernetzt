<script setup lang="ts">
import { computed } from 'vue'
import EntityAvatar from '@/components/EntityAvatar.vue'
import { initialsOf } from '@/services/avatars'
import { socialMediaPlatform } from '@/services/socialMedia'
import type { SocialMediaLinkDto } from '@/types/api'

/**
 * Overview shared by club, department and group detail pages: avatar, info text, address and contact details.
 * Groups have no own logo or address, so they hide the avatar column.
 */
const props = withDefaults(defineProps<{
  type: 'clubs' | 'departments'
  id: string
  name: string
  hasAvatar: boolean
  editable: boolean
  showAvatar?: boolean
  description?: string | null
  typeName?: string | null
  street?: string | null
  postalCode?: string | null
  city?: string | null
  email?: string | null
  website?: string | null
  socialMediaLinks?: SocialMediaLinkDto[] | null
}>(), { showAvatar: true })
defineEmits<{ edit: [] }>()
const address = computed(() => [props.street, [props.postalCode, props.city].filter(Boolean).join(' ')].filter(Boolean).join(', '))
const mapsUrl = computed(() => `https://www.openstreetmap.org/search?query=${encodeURIComponent(address.value)}`)
const socialLinks = computed(() => (props.socialMediaLinks ?? [])
  .filter(link => /^https?:\/\//i.test(link.url))
  .map(link => ({ ...link, info: socialMediaPlatform(link.platform) }))
  .filter(link => !!link.info))
const hasDetails = computed(() => !!(address.value || props.email || props.website || socialLinks.value.length))
const descriptionParts = computed(() => linkify(props.description ?? ''))

/** Plain text with line breaks; http(s) URLs become links. No HTML is ever rendered. */
function linkify(text: string): Array<{ text: string; href?: string }> {
  const parts: Array<{ text: string; href?: string }> = []
  const pattern = /https?:\/\/[^\s<>"]+[^\s<>".,;:!?)]/g
  let last = 0
  for (const match of text.matchAll(pattern)) {
    const start = match.index ?? 0
    if (start > last) parts.push({ text: text.slice(last, start) })
    parts.push({ text: match[0], href: match[0] })
    last = start + match[0].length
  }
  if (last < text.length) parts.push({ text: text.slice(last) })
  return parts
}
</script>

<template>
  <v-card v-if="description?.trim()" class="mb-6">
    <v-card-item title="Infos" prepend-icon="mdi-text-box-outline">
      <template v-if="editable" #append><v-btn variant="text" size="small" prepend-icon="mdi-pencil" @click="$emit('edit')">Bearbeiten</v-btn></template>
    </v-card-item>
    <v-card-text class="description text-body-large">
      <template v-for="(part, index) in descriptionParts" :key="index">
        <a v-if="part.href" :href="part.href" target="_blank" rel="noopener noreferrer">{{ part.text }}</a>
        <template v-else>{{ part.text }}</template>
      </template>
    </v-card-text>
  </v-card>
  <v-row>
    <v-col v-if="showAvatar" cols="12" md="4">
      <v-card class="pa-5 d-flex flex-column align-center justify-center" height="100%">
        <EntityAvatar :type="type" :id="id" :has-avatar="hasAvatar" :initials="initialsOf(name)" :editable="editable" />
        <v-chip v-if="typeName" size="small" variant="tonal" class="mt-4">{{ typeName }}</v-chip>
      </v-card>
    </v-col>
    <v-col cols="12" :md="showAvatar ? 8 : 12">
      <v-card height="100%">
        <v-card-item title="Kontakt und Adresse">
          <template v-if="editable" #append><v-btn variant="text" size="small" prepend-icon="mdi-pencil" @click="$emit('edit')">Bearbeiten</v-btn></template>
        </v-card-item>
        <v-list v-if="hasDetails">
          <v-list-item v-if="address" prepend-icon="mdi-map-marker-outline" :title="address" subtitle="Auf Karte anzeigen" :href="mapsUrl" target="_blank" rel="noopener noreferrer" />
          <v-list-item v-if="email" prepend-icon="mdi-email-outline" :title="email" subtitle="E-Mail schreiben" :href="`mailto:${email}`" />
          <v-list-item v-if="website" prepend-icon="mdi-web" :title="website" subtitle="Webseite öffnen" :href="website" target="_blank" rel="noopener noreferrer" />
          <v-list-item v-if="socialLinks.length" prepend-icon="mdi-share-variant-outline" subtitle="Social Media">
            <div class="d-flex flex-wrap ga-2 mt-1">
              <v-btn
                v-for="link in socialLinks" :key="link.url" :icon="`svg:${link.info!.path}`" :href="link.url" target="_blank" rel="noopener noreferrer"
                variant="tonal" size="small" :aria-label="`${link.info!.label}: ${link.url}`" v-tooltip:bottom="link.info!.label"
              />
            </div>
          </v-list-item>
        </v-list>
        <v-card-text v-else class="text-medium-emphasis">
          Keine Kontaktdaten hinterlegt.
          <v-btn v-if="editable" variant="tonal" size="small" prepend-icon="mdi-plus" class="d-flex mt-3" @click="$emit('edit')">Kontaktdaten ergänzen</v-btn>
        </v-card-text>
      </v-card>
    </v-col>
  </v-row>
</template>

<style scoped>
.description { white-space: pre-line; overflow-wrap: anywhere; }
</style>
