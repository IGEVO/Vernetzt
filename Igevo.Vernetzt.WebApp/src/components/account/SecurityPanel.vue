<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { api } from '@/services/api'
import { formatDateTime } from '@/services/dates'
import { passkeysSupported, registerPasskey } from '@/services/passkeys'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { PasskeyInfo } from '@/types/api'

const auth = useAuthStore(); const ui = useUiStore(); const router = useRouter()
const supported = passkeysSupported()
const name = ref(''); const adding = ref(false); const loggingOut = ref(false); const deletePasskey = ref<PasskeyInfo | null>(null); const confirmLogoutAll = ref(false)
const passkeys = useQuery({ queryKey: ['passkeys'], queryFn: ({ signal }) => api.get<PasskeyInfo[]>('/api/auth/passkeys', signal), enabled: supported })

async function addPasskey(): Promise<void> {
  adding.value = true
  try { await registerPasskey(name.value); name.value = ''; await passkeys.refetch(); ui.notify('Passkey wurde eingerichtet.', 'success') }
  catch (reason) { if (!(reason instanceof DOMException && ['NotAllowedError', 'AbortError'].includes(reason.name))) ui.notify('Passkey konnte nicht eingerichtet werden.', 'error') }
  finally { adding.value = false }
}
async function removePasskey(): Promise<void> {
  if (!deletePasskey.value) return
  try { await api.delete(`/api/auth/passkeys/${encodeURIComponent(deletePasskey.value.credentialId)}`); deletePasskey.value = null; await passkeys.refetch(); ui.notify('Passkey wurde entfernt.', 'success') }
  catch { ui.notify('Passkey konnte nicht entfernt werden.', 'error') }
}
async function logoutEverywhere(): Promise<void> {
  loggingOut.value = true
  try { await auth.logoutEverywhere(); await router.replace('/login') }
  catch { ui.notify('Abmelden auf allen Geräten ist fehlgeschlagen.', 'error') }
  finally { loggingOut.value = false }
}
</script>

<template>
  <v-card class="mb-6">
    <v-card-item title="Passkeys" subtitle="Anmelden mit Fingerabdruck, Gesicht oder Geräte-PIN" prepend-icon="mdi-fingerprint" />
    <v-card-text>
      <v-alert v-if="!supported" type="info" variant="tonal">Dieser Browser unterstützt keine Passkeys.</v-alert>
      <template v-else>
        <v-alert v-if="passkeys.isError.value" type="warning" variant="tonal" class="mb-4">Deine Passkeys konnten nicht geladen werden.</v-alert>
        <v-list v-else-if="passkeys.data.value?.length" class="mb-4 border rounded">
          <v-list-item v-for="item in passkeys.data.value" :key="item.credentialId" prepend-icon="mdi-key" :title="item.name || 'Passkey'" :subtitle="`Eingerichtet am ${formatDateTime(item.createdAt)}`">
            <template #append><v-btn icon="mdi-delete" color="error" variant="text" :aria-label="`Passkey ${item.name || ''} löschen`" @click="deletePasskey = item" /></template>
          </v-list-item>
        </v-list>
        <p v-else-if="passkeys.isSuccess.value" class="mb-4 text-medium-emphasis">Du hast noch keinen Passkey eingerichtet.</p>
        <v-form class="d-flex flex-wrap align-start ga-3" @submit.prevent="addPasskey">
          <v-text-field v-model="name" label="Name für den neuen Passkey (optional)" hint="z. B. iPhone, MacBook oder Windows-Laptop" persistent-hint class="flex-grow-1" style="min-width: 240px" />
          <v-btn type="submit" color="primary" size="large" prepend-icon="mdi-key-plus" :loading="adding">Passkey einrichten</v-btn>
        </v-form>
      </template>
    </v-card-text>
  </v-card>

  <v-card>
    <v-card-item title="Angemeldete Geräte" prepend-icon="mdi-devices" />
    <v-card-text>
      <p class="mb-4">Wenn du ein Gerät verloren hast oder dich an einem fremden Gerät angemeldet hast, kannst du dich überall abmelden. Andere Geräte werden innerhalb weniger Minuten abgemeldet; offene Anmeldelinks werden ungültig. Ein eingerichteter Adressbuch-Zugang (CardDAV) wird ebenfalls widerrufen.</p>
      <v-btn variant="outlined" prepend-icon="mdi-logout-variant" :loading="loggingOut" @click="confirmLogoutAll = true">Auf allen Geräten abmelden</v-btn>
    </v-card-text>
  </v-card>

  <ConfirmDialog :model-value="!!deletePasskey" title="Passkey löschen?" text="Der Passkey kann danach nicht mehr zur Anmeldung verwendet werden." @update:model-value="!$event && (deletePasskey = null)" @confirm="removePasskey" />
  <ConfirmDialog v-model="confirmLogoutAll" title="Auf allen Geräten abmelden?" text="Du wirst auch auf diesem Gerät abgemeldet. Dein Adressbuch-Zugang wird widerrufen; richte ihn danach bei Bedarf neu ein." confirm-text="Abmelden" confirm-color="primary" @confirm="confirmLogoutAll = false; logoutEverywhere()" />
</template>
