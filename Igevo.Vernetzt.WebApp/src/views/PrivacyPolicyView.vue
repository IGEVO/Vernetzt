<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { api } from '@/services/api'
import { sanitizeEmailHtml } from '@/services/html'
import type { PublicPrivacyPolicyDto } from '@/types/api'

/** Public privacy policy or imprint (no sign-in): own text from the settings, or a forward to the external page. */
const props = defineProps<{ imprint?: boolean }>()
const title = props.imprint ? 'Impressum' : 'Datenschutzerklärung'
const policy = ref<PublicPrivacyPolicyDto | null>(null)
const loading = ref(true)
const failed = ref(false)
const html = computed(() => policy.value?.mode === 'text' && policy.value.html ? sanitizeEmailHtml(policy.value.html) : '')

onMounted(async () => {
  try {
    policy.value = await api.get<PublicPrivacyPolicyDto>(props.imprint ? '/api/settings/imprint' : '/api/settings/privacy-policy')
    if (policy.value.mode === 'link' && policy.value.url) window.location.replace(policy.value.url)
  } catch { failed.value = true }
  finally { loading.value = false }
})
</script>

<template>
  <v-container class="pa-4">
    <v-card width="100%" max-width="860" class="mx-auto pa-4 pa-sm-8">
      <h1 class="text-headline-medium mb-4">{{ title }}</h1>
      <v-progress-linear v-if="loading" indeterminate color="primary" />
      <v-alert v-else-if="failed" type="error" variant="tonal">{{ imprint ? 'Das Impressum' : 'Die Datenschutzerklärung' }} konnte nicht geladen werden.</v-alert>
      <!-- eslint-disable vue/no-v-html -- sanitized on the server and again with DOMPurify (strict tag/attribute allowlist) -->
      <div v-else-if="html" class="privacy-text" v-html="html" />
      <!-- eslint-enable vue/no-v-html -->
      <p v-else-if="policy?.mode === 'link' && policy.url">
        Du wirst weitergeleitet. Falls nicht: <a :href="policy.url" rel="noopener">{{ title }} öffnen</a>
      </p>
      <v-alert v-else type="info" variant="tonal">{{ imprint ? 'Es ist noch kein Impressum hinterlegt.' : 'Es ist noch keine Datenschutzerklärung hinterlegt.' }}</v-alert>
      <div class="mt-6"><v-btn variant="text" prepend-icon="mdi-arrow-left" to="/">Zu Vernetzt</v-btn></div>
    </v-card>
  </v-container>
</template>

<style scoped>
.privacy-text :deep(h1), .privacy-text :deep(h2), .privacy-text :deep(h3) { margin: 1.5rem 0 0.5rem; line-height: 1.3; }
.privacy-text :deep(h1) { font-size: 1.5rem; }
.privacy-text :deep(h2) { font-size: 1.25rem; }
.privacy-text :deep(h3) { font-size: 1.1rem; }
.privacy-text :deep(p) { margin-bottom: 0.75rem; }
.privacy-text :deep(ul), .privacy-text :deep(ol) { padding-left: 1.5rem; margin-bottom: 0.75rem; }
.privacy-text :deep(a) { color: rgb(var(--v-theme-primary)); }
</style>
