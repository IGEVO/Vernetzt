<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import EmailChangeDialog from '@/components/EmailChangeDialog.vue'
import EntityAvatar from '@/components/EntityAvatar.vue'
import { api } from '@/services/api'
import { initialsOf } from '@/services/avatars'
import { visibilityDisplay, visibilityOptions } from '@/services/visibility'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { UserDto, VisibilityLevel } from '@/types/api'

const auth = useAuthStore(); const ui = useUiStore()
const editing = ref(false); const saving = ref(false); const changingEmail = ref(false)
const user = useQuery({ queryKey: ['user', 'me'], queryFn: ({ signal }) => api.get<UserDto>('/api/users/me', signal) })
const form = reactive({ firstName: '', lastName: '', phoneNumber: '', emailVisibility: 1 as VisibilityLevel, phoneVisibility: 1 as VisibilityLevel })

function edit(): void {
  const value = user.data.value
  if (!value) return
  Object.assign(form, { firstName: value.firstName, lastName: value.lastName, phoneNumber: value.phoneNumber ?? '', emailVisibility: value.emailVisibility, phoneVisibility: value.phoneVisibility })
  editing.value = true
}
async function save(): Promise<void> {
  if (!user.data.value || !form.firstName.trim() || !form.lastName.trim()) return
  saving.value = true
  try {
    await api.put<UserDto>(`/api/users/${user.data.value.id}`, { ...form, phoneNumber: form.phoneNumber || null })
    editing.value = false
    await Promise.all([user.refetch(), auth.initialize(true)])
    ui.notify('Profil wurde aktualisiert.', 'success')
  } catch { ui.notify('Profil konnte nicht gespeichert werden.', 'error') }
  finally { saving.value = false }
}
</script>

<template>
  <v-progress-linear v-if="user.isPending.value" indeterminate color="primary" />
  <v-alert v-else-if="user.isError.value" type="error">Profil konnte nicht geladen werden.</v-alert>
  <v-row v-else-if="user.data.value">
    <v-col cols="12" md="4">
      <v-card class="pa-5" height="100%">
        <EntityAvatar type="users" :has-avatar="user.data.value.hasAvatar" :initials="initialsOf(user.data.value.firstName, user.data.value.lastName)" editable />
      </v-card>
    </v-col>
    <v-col cols="12" md="8">
      <v-card height="100%">
        <v-card-item title="Persönliche Informationen">
          <template v-if="!editing" #append><v-btn variant="tonal" prepend-icon="mdi-pencil" @click="edit">Bearbeiten</v-btn></template>
        </v-card-item>
        <v-form v-if="editing" @submit.prevent="save">
          <v-card-text>
            <v-row dense>
              <v-col cols="12" sm="6"><v-text-field v-model="form.firstName" label="Vorname *" :rules="[(v: string) => !!v.trim() || 'Pflichtfeld']" /></v-col>
              <v-col cols="12" sm="6"><v-text-field v-model="form.lastName" label="Nachname *" :rules="[(v: string) => !!v.trim() || 'Pflichtfeld']" /></v-col>
            </v-row>
            <div class="d-flex align-start ga-2 mb-2">
              <v-text-field :model-value="user.data.value.email" label="Persönliche E-Mail (Anmeldung)" disabled hint="Gilt für alle Zuordnungen, solange dort keine Amtsadresse hinterlegt ist." persistent-hint />
              <v-btn variant="tonal" class="mt-2" prepend-icon="mdi-email-edit-outline" @click="changingEmail = true">Ändern</v-btn>
            </div>
            <v-text-field v-model="form.phoneNumber" label="Telefonnummer" type="tel" />
            <v-row dense>
              <v-col cols="12" sm="6"><v-select v-model="form.emailVisibility" label="Persönliche E-Mail sichtbar für" :items="visibilityOptions" /></v-col>
              <v-col cols="12" sm="6"><v-select v-model="form.phoneVisibility" label="Telefon sichtbar für" :items="visibilityOptions" /></v-col>
            </v-row>
          </v-card-text>
          <v-card-actions><v-spacer /><v-btn :disabled="saving" @click="editing = false">Abbrechen</v-btn><v-btn type="submit" color="primary" variant="flat" :loading="saving">Speichern</v-btn></v-card-actions>
        </v-form>
        <v-list v-else>
          <v-list-item prepend-icon="mdi-account" :title="`${user.data.value.firstName} ${user.data.value.lastName}`" />
          <v-list-item prepend-icon="mdi-email" :title="user.data.value.email" subtitle="Persönliche E-Mail (Anmeldung)">
            <div class="mt-2">
              <v-chip size="small" variant="tonal" :color="visibilityDisplay(user.data.value.emailVisibility).color" :prepend-icon="visibilityDisplay(user.data.value.emailVisibility).icon" title="Wer deine persönliche E-Mail sehen kann">
                Sichtbar für: {{ visibilityDisplay(user.data.value.emailVisibility).title }}
              </v-chip>
            </div>
          </v-list-item>
          <v-list-item class="text-body-medium text-medium-emphasis" prepend-icon="mdi-briefcase-outline">
            Mails zu deinen Zuordnungen gehen an diese Adresse, solange du dort keine Amtsadresse hinterlegst.
            <RouterLink to="/konto?tab=zuordnungen">Amtsadressen verwalten</RouterLink>
          </v-list-item>
          <v-list-item prepend-icon="mdi-phone" :title="user.data.value.phoneNumber || 'Keine Telefonnummer hinterlegt'">
            <div v-if="user.data.value.phoneNumber" class="mt-2">
              <v-chip size="small" variant="tonal" :color="visibilityDisplay(user.data.value.phoneVisibility).color" :prepend-icon="visibilityDisplay(user.data.value.phoneVisibility).icon" title="Wer deine Telefonnummer sehen kann">
                Sichtbar für: {{ visibilityDisplay(user.data.value.phoneVisibility).title }}
              </v-chip>
            </div>
          </v-list-item>
        </v-list>
      </v-card>
    </v-col>
  </v-row>
  <EmailChangeDialog v-if="user.data.value" v-model="changingEmail" user-id="me" :current-email="user.data.value.email" />
</template>
