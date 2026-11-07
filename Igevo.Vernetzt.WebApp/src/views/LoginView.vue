<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, ApiError } from '@/services/api'
import { passkeysSupported, requestPasskey } from '@/services/passkeys'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const email = ref('')
const busy = ref(false)
const sent = ref(false)
const error = ref('')
const verifying = ref(false)
const touched = ref(false)
const passkeyAvailable = passkeysSupported()
const returnUrl = computed(() => normalizeReturnUrl(typeof route.query.returnUrl === 'string' ? route.query.returnUrl : undefined))
const tooManyAttempts = 'Zu viele Anmeldeversuche in kurzer Zeit. Bitte warte eine Minute und versuche es dann erneut.'
const isRateLimited = (reason: unknown) => reason instanceof ApiError && reason.status === 429
const emailValid = computed(() => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.value))
// The server keeps exactly one pending passkey challenge per browser, so the background
// autofill request must be cancelled before an explicit passkey sign-in starts.
const conditionalRequest = new AbortController()
onBeforeUnmount(() => conditionalRequest.abort())

onMounted(async () => {
  const token = typeof route.query.token === 'string' ? route.query.token : ''
  const userId = typeof route.query.uid === 'string' ? route.query.uid : ''
  if (!token || !userId) {
    if (passkeyAvailable && await PublicKeyCredential.isConditionalMediationAvailable?.()) {
      try {
        const options = await api.post<string>('/api/auth/passkeys/request-options')
        await auth.signInWithPasskey(await requestPasskey(options, true, conditionalRequest.signal))
        await router.replace(returnUrl.value ?? '/')
      } catch { /* Conditional mediation is optional and stays silent until the user chooses a passkey. */ }
    }
    return
  }
  verifying.value = true
  try { await auth.verifyLoginLink(userId, token); await router.replace(returnUrl.value ?? '/') }
  catch (reason) { error.value = isRateLimited(reason) ? `${tooManyAttempts} Lade die Seite dazu einfach neu – der Link bleibt gültig, solange er nicht abgelaufen ist.` : 'Der Anmeldelink ist ungültig, abgelaufen oder wurde bereits verwendet. Bitte fordere einen neuen an.' }
  finally { verifying.value = false }
})

function normalizeReturnUrl(value?: string): string | undefined {
  return value?.startsWith('/') && !value.startsWith('//') ? value : undefined
}
async function requestLink(): Promise<void> {
  touched.value = true
  if (!emailValid.value || busy.value) return
  busy.value = true; error.value = ''
  try { await auth.requestLoginLink(email.value, returnUrl.value); sent.value = true }
  catch (reason) { error.value = isRateLimited(reason) ? tooManyAttempts : 'Der Anmeldelink konnte nicht gesendet werden. Bitte versuche es später erneut.' }
  finally { busy.value = false }
}
function submitOnEnter(event: KeyboardEvent): void {
  if (event.isComposing) return
  event.preventDefault()
  void requestLink()
}
async function signInPasskey(): Promise<void> {
  conditionalRequest.abort()
  busy.value = true; error.value = ''
  try {
    const options = await api.post<string>('/api/auth/passkeys/request-options')
    await auth.signInWithPasskey(await requestPasskey(options))
    await router.replace(returnUrl.value ?? '/')
  } catch (reason) {
    error.value = reason instanceof DOMException && ['NotAllowedError', 'AbortError'].includes(reason.name)
      ? 'Passkey-Anmeldung wurde abgebrochen oder es war kein passender Passkey verfügbar.'
      : 'Die Passkey-Anmeldung ist fehlgeschlagen.'
  } finally { busy.value = false }
}
</script>

<template>
  <v-container class="public-page pa-4">
    <v-card width="100%" max-width="500" elevation="5" class="pa-4 pa-sm-8">
      <v-card-title class="text-center"><v-img src="/images/logo-no-bg.svg" height="80" alt="Vernetzt" /><h1 class="text-headline-large mt-3">Vernetzt</h1></v-card-title>
      <v-card-text>
        <v-progress-linear v-if="verifying" indeterminate color="secondary" class="mb-4" />
        <v-alert v-if="error" type="error" closable class="mb-4" @click:close="error = ''">{{ error }}</v-alert>
        <v-alert v-if="sent" type="success" class="mb-4">
          Wir haben dir einen Anmeldelink an <strong>{{ email }}</strong> gesendet. Bitte prüfe auch den Spam-Ordner.
          <div class="mt-2"><v-btn variant="text" size="small" class="px-0" @click="sent = false">Andere E-Mail-Adresse verwenden</v-btn></div>
        </v-alert>
        <!-- No :disabled on the submit button: a disabled default button blocks submitting with Enter.
             Enter is also handled explicitly on the field: with passkey autofill (autocomplete="webauthn")
             some browsers swallow the implicit form submission while a conditional passkey request is pending. -->
        <v-form v-else @submit.prevent="requestLink">
          <v-text-field v-model.trim="email" label="E-Mail-Adresse" type="email" autocomplete="email webauthn" prepend-inner-icon="mdi-email" :error-messages="touched && !emailValid ? 'Bitte eine gültige E-Mail-Adresse eingeben' : undefined" @update:model-value="touched = false" @keydown.enter="submitOnEnter" />
          <v-btn type="submit" color="primary" block size="large" :loading="busy">Anmeldelink senden</v-btn>
          <template v-if="passkeyAvailable">
            <v-divider class="my-5">oder</v-divider>
            <v-btn block size="large" variant="outlined" prepend-icon="mdi-key" :disabled="busy" @click="signInPasskey">Mit Passkey anmelden</v-btn>
          </template>
        </v-form>
      </v-card-text>
    </v-card>
  </v-container>
</template>
