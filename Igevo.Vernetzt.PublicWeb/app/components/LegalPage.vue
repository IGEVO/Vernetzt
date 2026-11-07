<script setup lang="ts">
import type { PublicPortalLegalPage } from '~/types/public'

const props = defineProps<{
  title: string
  path: string
  page: 'imprint' | 'privacy'
}>()

const nuxtApp = useNuxtApp()
const { data: portal } = await usePortal()
const content = computed<PublicPortalLegalPage | undefined>(() => portal.value?.[props.page])

const externalUrl = content.value?.mode === 'link' ? content.value.url : null
if (externalUrl) {
  await nuxtApp.runWithContext(() => navigateTo(externalUrl, { external: true, redirectCode: 302 }))
}

useCanonical(props.path)
useSeoMeta({ title: props.title })
</script>

<template>
  <div class="container narrow page legal">
    <h1>{{ title }}</h1>
    <!-- eslint-disable-next-line vue/no-v-html -- sanitized by the API on save (allowlist in PortalSettingsService) -->
    <div v-if="content?.html" class="legal-content" v-html="content.html" />
    <p v-else class="notice">Diese Angaben wurden noch nicht hinterlegt.</p>
  </div>
</template>
