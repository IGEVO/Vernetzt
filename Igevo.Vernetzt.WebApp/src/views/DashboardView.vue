<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import AvatarImage from '@/components/AvatarImage.vue'
import PageHeader from '@/components/PageHeader.vue'
import { api } from '@/services/api'
import { initialsOf } from '@/services/avatars'
import { passkeysSupported, registerPasskey } from '@/services/passkeys'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { DashboardDataDto, PasskeyInfo } from '@/types/api'

const auth = useAuthStore()
const ui = useUiStore()
const dashboard = useQuery({ queryKey: ['dashboard'], queryFn: ({ signal }) => api.get<DashboardDataDto>('/api/stats/dashboard', signal) })

// Gentle, dismissible passkey hint for users without a passkey (hidden for 30 days after "Später").
const passkeyHintKey = 'passkey_hint_dismissed_until'
const passkeyAvailable = passkeysSupported()
const passkeys = useQuery({ queryKey: ['passkeys'], queryFn: ({ signal }) => api.get<PasskeyInfo[]>('/api/auth/passkeys', signal), enabled: passkeyAvailable })
const hintDismissed = ref(readHintDismissed())
const passkeyBusy = ref(false)
const showPasskeyHint = computed(() => passkeyAvailable && !hintDismissed.value && passkeys.isSuccess.value && (passkeys.data.value?.length ?? 0) === 0)
function readHintDismissed(): boolean {
  try { return Number(localStorage.getItem(passkeyHintKey) ?? 0) > Date.now() } catch { return false }
}
function dismissPasskeyHint(): void {
  try { localStorage.setItem(passkeyHintKey, String(Date.now() + 30 * 24 * 60 * 60 * 1000)) } catch { /* storage unavailable */ }
  hintDismissed.value = true
}
async function setUpPasskey(): Promise<void> {
  passkeyBusy.value = true
  try { await registerPasskey(); await passkeys.refetch(); ui.notify('Passkey wurde eingerichtet.', 'success') }
  catch { ui.notify('Passkey konnte nicht eingerichtet werden.', 'error') }
  finally { passkeyBusy.value = false }
}
const links = [
  { title: 'Vereine', text: 'Vereine und Abteilungen durchsuchen', icon: 'mdi-account-group', to: '/clubs' },
  { title: 'Kontakte', text: 'Ansprechpersonen finden', icon: 'mdi-card-account-details', to: '/kontakte' },
  { title: 'E-Mail versenden', text: 'Kontakte gesammelt erreichen', icon: 'mdi-email', to: '/emails/send' },
  { title: 'Kalender', text: 'Aktuelle Termine ansehen', icon: 'mdi-calendar-month', to: '/calendar/main' },
  { title: 'Mein Konto', text: 'Profil, Passkeys und Datenschutz', icon: 'mdi-account-cog', to: '/konto' }
]
</script>

<template>
  <PageHeader :title="dashboard.data.value ? `Hallo ${dashboard.data.value.userDisplayName}!` : 'Dashboard'" subtitle="Schön, dass du da bist." />
  <v-progress-linear v-if="dashboard.isPending.value" indeterminate color="primary" />
  <v-alert v-else-if="dashboard.isError.value" type="error">Das Dashboard konnte nicht geladen werden.</v-alert>
  <template v-else-if="dashboard.data.value">
    <v-alert v-if="showPasskeyHint" type="info" variant="tonal" icon="mdi-fingerprint" class="mb-6" closable close-label="Später erinnern" @click:close="dismissPasskeyHint">
      <div class="font-weight-bold">Schneller anmelden mit Passkey</div>
      <div class="mb-3">Melde dich künftig mit Fingerabdruck, Gesicht oder Geräte-PIN an – ohne auf eine E-Mail zu warten.</div>
      <v-btn color="primary" variant="flat" size="small" prepend-icon="mdi-key-plus" :loading="passkeyBusy" @click="setUpPasskey">Jetzt einrichten</v-btn>
      <v-btn variant="text" size="small" class="ml-2" @click="dismissPasskeyHint">Später</v-btn>
    </v-alert>
    <template v-if="dashboard.data.value.myClubs.length">
      <h2 class="text-headline-small mb-3">Meine Vereine, Abteilungen &amp; Gruppen</h2>
      <v-card class="mb-8">
        <v-list lines="one">
          <template v-for="(club, index) in dashboard.data.value.myClubs" :key="club.id">
            <v-divider v-if="index > 0" />
            <v-list-item :to="`/clubs/${club.id}`" :title="club.name" :subtitle="club.city ?? undefined">
              <template #prepend><AvatarImage type="clubs" :id="club.id" :initials="initialsOf(club.name)" class="mr-4" /></template>
              <template v-if="club.isContactPerson" #append><v-chip size="small" variant="tonal">Kontaktperson</v-chip></template>
            </v-list-item>
            <v-list-item v-for="department in club.departments" :key="department.id" :to="`/departments/${department.id}`" prepend-icon="mdi-source-branch" :title="department.name" :subtitle="department.parentDepartmentName ? `Gruppe in ${department.parentDepartmentName}` : undefined" class="pl-12">
              <template v-if="department.isContactPerson" #append><v-chip size="small" variant="tonal">Kontaktperson</v-chip></template>
            </v-list-item>
          </template>
        </v-list>
      </v-card>
    </template>
    <h2 class="text-headline-small mb-3">Schnellzugriff</h2>
    <v-row>
      <v-col v-for="link in links" :key="link.to" cols="12" sm="6" lg="4">
        <v-card :to="link.to" height="100%" hover><v-card-text class="d-flex ga-4 align-center"><v-avatar color="primary" variant="tonal" size="52"><v-icon :icon="link.icon" size="28" /></v-avatar><div><div class="text-title-large">{{ link.title }}</div><div class="text-medium-emphasis">{{ link.text }}</div></div></v-card-text></v-card>
      </v-col>
      <v-col v-if="auth.hasRole('SystemAdmin', 'Manager')" cols="12" sm="6" lg="4">
        <v-card to="/admin/users" height="100%" hover><v-card-text class="d-flex ga-4 align-center"><v-avatar color="primary" variant="tonal" size="52"><v-icon icon="mdi-shield-account" size="28" /></v-avatar><div><div class="text-title-large">Administration</div><div class="text-medium-emphasis">Benutzer und Stammdaten verwalten</div></div></v-card-text></v-card>
      </v-col>
    </v-row>
  </template>
</template>
