<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '@/services/api'
import { passkeysSupported, registerPasskey, requestPasskey } from '@/services/passkeys'
import { PASSKEY_REQUIRED_PATH, useAuthStore } from '@/stores/auth'

// Users with a privileged role (by default SystemAdmin and Manager) who signed in with a login link land here.
// The server only allows passkey setup and passkey sign-in until the session was started with a passkey.
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const passkeyAvailable = passkeysSupported()
const busy = ref(false)
const error = ref('')
const justRegistered = ref(false)
const returnUrl = computed(() => {
  const value = typeof route.query.returnUrl === 'string' ? route.query.returnUrl : ''
  return value.startsWith('/') && !value.startsWith('//') && !value.startsWith(PASSKEY_REQUIRED_PATH) ? value : '/'
})

onMounted(async () => {
  await auth.initialize(true)
  if (!auth.isAuthenticated) await router.replace({ path: '/login', query: { returnUrl: returnUrl.value } })
  else if (!auth.passkeyRequired) await router.replace(returnUrl.value)
})

function isCancelled(reason: unknown): boolean {
  return reason instanceof DOMException && ['NotAllowedError', 'AbortError'].includes(reason.name)
}

async function setUpPasskey(): Promise<void> {
  busy.value = true; error.value = ''
  try {
    await registerPasskey()
    auth.hasPasskey = true
    justRegistered.value = true
  } catch (reason) {
    error.value = isCancelled(reason)
      ? 'Die Einrichtung wurde abgebrochen. Bitte versuche es erneut.'
      : 'Der Passkey konnte nicht eingerichtet werden. Bitte versuche es erneut.'
  } finally { busy.value = false }
}

async function signInWithPasskey(): Promise<void> {
  busy.value = true; error.value = ''
  try {
    const options = await api.post<string>('/api/auth/passkeys/request-options')
    await auth.signInWithPasskey(await requestPasskey(options))
    if (auth.passkeyRequired) throw new Error('Session is still restricted.')
    await router.replace(returnUrl.value)
  } catch (reason) {
    error.value = isCancelled(reason)
      ? 'Die Anmeldung wurde abgebrochen oder es war kein passender Passkey verfügbar.'
      : 'Die Anmeldung mit Passkey ist fehlgeschlagen. Bitte versuche es erneut.'
  } finally { busy.value = false }
}

async function logout(): Promise<void> {
  await auth.logout()
  await router.replace('/login')
}
</script>

<template>
  <v-container class="public-page pa-4">
    <v-card width="100%" max-width="560" elevation="5" class="pa-4 pa-sm-8">
      <v-card-title class="d-flex align-center ga-3 text-wrap">
        <v-icon icon="mdi-shield-key" color="secondary" size="40" />
        <h1 class="text-headline-small" tabindex="-1">Anmeldung mit Passkey</h1>
      </v-card-title>
      <v-card-text>
        <p class="mb-4">
          Hallo {{ auth.user?.firstName }}, dein Konto hat <strong>erweiterte Rechte</strong> (SystemAdmin oder Manager).
          Zu ihrem Schutz musst du dich dafür mit einem Passkey anmelden – also mit Fingerabdruck, Gesicht oder der PIN deines Geräts.
          Der Anmeldelink per E-Mail reicht dafür nicht aus.
        </p>

        <v-alert v-if="error" type="error" closable class="mb-4" @click:close="error = ''">{{ error }}</v-alert>
        <v-alert v-if="!passkeyAvailable" type="warning" class="mb-4">
          Dieser Browser unterstützt keine Passkeys. Bitte öffne Vernetzt in einem aktuellen Browser (z. B. Chrome, Edge, Safari oder Firefox).
        </v-alert>

        <template v-else-if="!auth.hasPasskey">
          <p class="mb-4">Du hast noch keinen Passkey. Richte jetzt einen ein – das dauert nur einen Moment.</p>
          <v-btn color="primary" size="large" block prepend-icon="mdi-fingerprint" :loading="busy" @click="setUpPasskey">Passkey einrichten</v-btn>
        </template>

        <template v-else>
          <v-alert v-if="justRegistered" type="success" class="mb-4">Dein Passkey ist eingerichtet. Melde dich jetzt einmal damit an.</v-alert>
          <v-btn color="primary" size="large" block prepend-icon="mdi-key" :loading="busy" @click="signInWithPasskey">Mit Passkey anmelden</v-btn>
          <p class="text-body-medium text-medium-emphasis mt-4">
            Passkey verloren oder neues Gerät? Eine Person mit der Rolle SystemAdmin kann deine Passkeys
            in der Benutzerverwaltung zurücksetzen. Danach kannst du hier einen neuen einrichten.
          </p>
        </template>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn variant="text" @click="logout">Abmelden</v-btn>
      </v-card-actions>
    </v-card>
  </v-container>
</template>
