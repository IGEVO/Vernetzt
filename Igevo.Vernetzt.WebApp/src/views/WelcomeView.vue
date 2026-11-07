<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { api } from '@/services/api'
import { passkeysSupported, registerPasskey } from '@/services/passkeys'
import { ROLE_EMAIL_HELP_URL } from '@/services/roleEmail'
import { useAuthStore } from '@/stores/auth'
import type { MyContactAssignmentDto, PasskeyInfo } from '@/types/api'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const passkeyAvailable = passkeysSupported()
const wantsPasskey = computed(() => route.query.einrichten === 'passkey')
const passkeys = useQuery({ queryKey: ['passkeys'], queryFn: ({ signal }) => api.get<PasskeyInfo[]>('/api/auth/passkeys', signal), enabled: passkeyAvailable })
const hasPasskey = computed(() => (passkeys.data.value?.length ?? 0) > 0)
// Contact persons often have a club / office address: suggest storing it per assignment instead of using it as login.
const assignments = useQuery({ queryKey: ['user', 'me', 'assignments'], queryFn: ({ signal }) => api.get<MyContactAssignmentDto[]>('/api/users/me/contact-assignments', signal) })
const assignmentNames = computed(() => [...new Set((assignments.data.value ?? []).map((item) => item.organizationName))].join(', '))
const unconfirmedRoleAddresses = computed(() => (assignments.data.value ?? []).filter((item) => item.contextEmail && !item.contextEmailVerified).map((item) => item.contextEmail as string))
const suggestRoleAddress = computed(() => (assignments.data.value?.length ?? 0) > 0 && (assignments.data.value ?? []).every((item) => !item.contextEmail))
const busy = ref(false)
const done = ref(false)
const error = ref('')

async function setUpPasskey(): Promise<void> {
  busy.value = true; error.value = ''
  try {
    await registerPasskey()
    await passkeys.refetch()
    done.value = true
  } catch (reason) {
    error.value = reason instanceof DOMException && ['NotAllowedError', 'AbortError'].includes(reason.name)
      ? 'Die Einrichtung wurde abgebrochen. Du kannst es jederzeit erneut versuchen.'
      : 'Der Passkey konnte nicht eingerichtet werden. Du kannst es später unter „Mein Konto“ erneut versuchen.'
  } finally { busy.value = false }
}
function goToDashboard(): void { void router.replace('/') }
</script>

<template>
  <v-container class="pa-0" style="max-width: 760px">
    <h1 class="text-headline-large page-title" tabindex="-1">Willkommen, {{ auth.user?.firstName }}!</h1>
    <p class="text-title-large font-weight-regular mb-6">Schön, dass du dabei bist. Du bist jetzt angemeldet.</p>

    <v-card v-if="passkeyAvailable && !hasPasskey && !done" :variant="wantsPasskey ? 'elevated' : 'outlined'" class="mb-6">
      <v-card-title class="d-flex align-center ga-3"><v-icon icon="mdi-fingerprint" color="secondary" size="36" />Schneller anmelden mit Passkey</v-card-title>
      <v-card-text>
        <p class="mb-3">Mit einem Passkey meldest du dich künftig mit <strong>Fingerabdruck, Gesicht oder der PIN deines Geräts</strong> an – ohne auf eine E-Mail zu warten.</p>
        <ul class="ml-5 mb-3">
          <li>Dein Gerät fragt gleich nach deinem Fingerabdruck, Gesicht oder deiner PIN.</li>
          <li>Es wird kein Passwort angelegt und nichts davon verlässt dein Gerät.</li>
          <li>Der Anmeldelink per E-Mail funktioniert weiterhin.</li>
        </ul>
        <v-alert v-if="error" type="warning" variant="tonal" class="mb-3">{{ error }}</v-alert>
      </v-card-text>
      <v-card-actions class="flex-wrap ga-2 px-4 pb-4">
        <v-btn color="primary" variant="flat" size="large" prepend-icon="mdi-key-plus" :loading="busy" @click="setUpPasskey">Passkey jetzt einrichten</v-btn>
        <v-btn variant="text" size="large" @click="goToDashboard">Später</v-btn>
      </v-card-actions>
    </v-card>

    <v-alert v-if="done" type="success" icon="mdi-check-circle" class="mb-6">
      <div class="text-title-large">Fertig!</div>
      Beim nächsten Mal reicht dein Fingerabdruck, dein Gesicht oder deine Geräte-PIN.
    </v-alert>

    <v-alert v-if="unconfirmedRoleAddresses.length" type="info" variant="tonal" icon="mdi-email-check-outline" class="mb-6">
      Bitte bestätige noch deine Amtsadresse{{ unconfirmedRoleAddresses.length > 1 ? 'n' : '' }} <strong>{{ unconfirmedRoleAddresses.join(', ') }}</strong> über den Link in der Bestätigungs-E-Mail. Bis dahin gehen Mails zu der Zuordnung an deine persönliche Adresse.
    </v-alert>

    <v-card v-if="suggestRoleAddress" variant="outlined" class="mb-6">
      <v-card-title class="d-flex align-center ga-3 text-wrap"><v-icon icon="mdi-briefcase-outline" color="secondary" size="36" />Vereins- oder Amtsadresse hinterlegen?</v-card-title>
      <v-card-text>
        <p class="mb-3">Du bist als Kontaktperson bei <strong>{{ assignmentNames }}</strong> eingetragen. Mails dazu gehen an deine persönliche Adresse <strong>{{ auth.user?.email }}</strong>.</p>
        <p class="mb-0">Hast du für dein Amt eine eigene Adresse, z. B. vorstand@verein.de? Dann hinterlege sie für die passende Zuordnung. Bei einem Amtswechsel bleibt dein Konto so bei dir und die Adresse geht an die Nachfolge.</p>
      </v-card-text>
      <v-card-actions class="flex-wrap ga-2 px-4 pb-4">
        <v-btn color="primary" variant="tonal" prepend-icon="mdi-briefcase-plus-outline" to="/konto?tab=zuordnungen">Amtsadresse hinterlegen</v-btn>
        <v-btn variant="text" :href="ROLE_EMAIL_HELP_URL" target="_blank" rel="noopener noreferrer" append-icon="mdi-open-in-new">Mehr erfahren</v-btn>
      </v-card-actions>
    </v-card>

    <v-btn v-if="done || hasPasskey || !passkeyAvailable" color="primary" size="large" variant="flat" append-icon="mdi-arrow-right" @click="goToDashboard">Los geht's</v-btn>
  </v-container>
</template>
