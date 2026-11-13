<script setup lang="ts">
import { ref, watch } from 'vue'
import { api, ApiError } from '@/services/api'
import { useUiStore } from '@/stores/ui'

/**
 * Requests a change of the personal (login) address. The address only changes once
 * the confirmation link sent to the new address has been opened.
 */
const props = defineProps<{
  /** 'me' for the own account, otherwise the id of the user being administered */
  userId: string
  currentEmail: string
}>()
const open = defineModel<boolean>({ required: true })

const ui = useUiStore()
const newEmail = ref('')
const error = ref('')
const sending = ref(false)
const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

watch(open, (value) => { if (value) { newEmail.value = ''; error.value = '' } })

async function submit(): Promise<void> {
  const address = newEmail.value.trim()
  if (!emailPattern.test(address)) { error.value = 'Bitte eine gültige E-Mail-Adresse eingeben.'; return }
  if (address.toLocaleLowerCase() === props.currentEmail.toLocaleLowerCase()) { error.value = 'Das ist bereits die aktuelle Adresse.'; return }
  sending.value = true
  error.value = ''
  try {
    await api.post(`/api/users/${props.userId}/email-change`, { newEmail: address })
    open.value = false
    ui.notify(`Bestätigungslink an ${address} gesendet. Die Adresse ändert sich erst, wenn der Link geöffnet wurde.`, 'success')
  } catch (reason) {
    error.value = reason instanceof ApiError && reason.message ? reason.message : 'Die Änderung konnte nicht angefordert werden.'
  } finally { sending.value = false }
}
</script>

<template>
  <v-dialog v-model="open" max-width="520">
    <v-card title="Anmeldeadresse ändern">
      <v-form @submit.prevent="submit">
        <v-card-text>
          <p class="mb-4">Aktuell: <strong>{{ currentEmail }}</strong></p>
          <v-text-field v-model="newEmail" label="Neue E-Mail-Adresse" type="email" autocomplete="email" autofocus :error-messages="error" />
          <v-alert type="info" variant="tonal" density="compact" class="mt-2">
            An die neue Adresse geht ein Bestätigungslink (72 Stunden gültig). Erst danach wird sie zur Anmeldung genutzt;
            die bisherige Adresse erhält einen Hinweis. Passkeys und bestehende Anmeldungen bleiben gültig.
          </v-alert>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn :disabled="sending" @click="open = false">Abbrechen</v-btn>
          <v-btn type="submit" color="primary" variant="flat" :loading="sending">Bestätigungslink senden</v-btn>
        </v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>
</template>
