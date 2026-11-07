<script setup lang="ts">
import { ref } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { api } from '@/services/api'
import { useUiStore } from '@/stores/ui'
import type { CalendarFeedTokenCreatedDto, CalendarFeedTokenDto } from '@/types/api'

/**
 * One subscribable calendar link (iCal feed): shows the URL with an explicit copy button and offers
 * create, renew and revoke – the latter two only after confirmation, since they break existing subscriptions.
 */
const props = defineProps<{ title: string; subtitle?: string; token?: CalendarFeedTokenDto; createBody?: Record<string, unknown>; readonly?: boolean }>()
const emit = defineEmits<{ changed: [] }>()
const ui = useUiStore()
const busy = ref(false)
const copied = ref(false)
const confirmRotate = ref(false)
const confirmRevoke = ref(false)

async function run(action: () => Promise<unknown>, success: string, failure: string): Promise<void> {
  busy.value = true
  try { await action(); emit('changed'); ui.notify(success, 'success') }
  catch { ui.notify(failure, 'error') }
  finally { busy.value = false }
}
const create = () => run(() => api.post<CalendarFeedTokenCreatedDto>('/api/calendar-feeds/tokens', props.createBody ?? {}), 'Kalender-Link wurde erstellt.', 'Kalender-Link konnte nicht erstellt werden.')
const rotate = () => { confirmRotate.value = false; return run(() => api.post(`/api/calendar-feeds/tokens/${props.token!.id}/rotate`), 'Kalender-Link wurde erneuert. Bitte aktualisiere alle Abonnements.', 'Kalender-Link konnte nicht erneuert werden.') }
const revoke = () => { confirmRevoke.value = false; return run(() => api.delete(`/api/calendar-feeds/tokens/${props.token!.id}`), 'Kalender-Link wurde widerrufen.', 'Kalender-Link konnte nicht widerrufen werden.') }

async function copy(): Promise<void> {
  if (!props.token?.feedUrl) return
  try {
    await navigator.clipboard.writeText(props.token.feedUrl)
    copied.value = true
    setTimeout(() => { copied.value = false }, 2000)
  } catch { ui.notify('Der Link konnte nicht kopiert werden. Bitte markiere ihn manuell.', 'warning') }
}
</script>

<template>
  <div class="feed-link">
    <div class="d-flex flex-wrap align-center ga-2 mb-3">
      <div class="min-w-0">
        <div class="text-title-medium">{{ title }}</div>
        <div v-if="subtitle" class="text-body-medium text-medium-emphasis">{{ subtitle }}</div>
      </div>
      <v-spacer />
      <v-btn v-if="!token && !readonly" size="small" variant="tonal" prepend-icon="mdi-plus" :loading="busy" @click="create">Link erstellen</v-btn>
    </div>
    <template v-if="token">
      <div class="d-flex align-center ga-2 mb-1">
        <v-text-field :model-value="token.feedUrl ?? ''" :aria-label="`Link: ${title}`" readonly hide-details density="compact" class="flex-grow-1 min-w-0" @focus="($event.target as HTMLInputElement).select()" />
        <v-btn :variant="copied ? 'tonal' : 'flat'" :color="copied ? 'success' : 'primary'" :prepend-icon="copied ? 'mdi-check' : 'mdi-content-copy'" min-width="120" @click="copy">{{ copied ? 'Kopiert' : 'Kopieren' }}</v-btn>
      </div>
      <div v-if="!readonly" class="d-flex flex-wrap ga-2">
        <v-btn size="small" variant="text" prepend-icon="mdi-refresh" :disabled="busy" @click="confirmRotate = true">Erneuern</v-btn>
        <v-btn size="small" variant="text" color="error" prepend-icon="mdi-link-off" :disabled="busy" @click="confirmRevoke = true">Widerrufen</v-btn>
      </div>
    </template>
    <p v-else-if="!readonly" class="text-body-medium text-medium-emphasis mb-0">Noch kein Link erstellt.</p>

    <ConfirmDialog v-model="confirmRotate" title="Kalender-Link erneuern?" text="Der bisherige Link wird ungültig. Alle bestehenden Kalender-Abonnements müssen mit dem neuen Link aktualisiert werden." confirm-text="Erneuern" confirm-color="primary" @confirm="rotate" />
    <ConfirmDialog v-model="confirmRevoke" title="Kalender-Link widerrufen?" text="Der Link wird dauerhaft ungültig. Bestehende Kalender-Abonnements erhalten keine Termine mehr." confirm-text="Widerrufen" @confirm="revoke" />
  </div>
</template>
