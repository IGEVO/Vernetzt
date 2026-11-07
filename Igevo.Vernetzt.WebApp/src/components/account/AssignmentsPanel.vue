<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { api } from '@/services/api'
import { contextEmailError, ROLE_EMAIL_HELP_URL } from '@/services/roleEmail'
import { visibilityOptions } from '@/services/visibility'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { MyContactAssignmentDto, VisibilityLevel } from '@/types/api'
import { organizationPath } from '@/services/organizationPath'

const ui = useUiStore(); const auth = useAuthStore(); const saving = ref('')
const assignments = useQuery({ queryKey: ['user', 'me', 'assignments'], queryFn: ({ signal }) => api.get<MyContactAssignmentDto[]>('/api/users/me/contact-assignments', signal) })
// Editable drafts per assignment: query results are read-only and must not be bound with v-model directly.
const drafts = reactive<Record<string, { contextEmail: string; contextEmailVisibility: VisibilityLevel }>>({})
watch(() => assignments.data.value, (items) => {
  for (const item of items ?? []) drafts[item.assignmentId] = { contextEmail: item.contextEmail ?? '', contextEmailVisibility: item.contextEmailVisibility }
}, { immediate: true })
const draftFor = (item: MyContactAssignmentDto) => (drafts[item.assignmentId] ??= { contextEmail: item.contextEmail ?? '', contextEmailVisibility: item.contextEmailVisibility })
const isDepartment = (item: MyContactAssignmentDto) => item.organizationType === 1 || item.organizationType === 'Department'
const link = (item: MyContactAssignmentDto) => `/${isDepartment(item) ? 'departments' : 'clubs'}/${item.organizationId}`
const draftError = (item: MyContactAssignmentDto) => contextEmailError(draftFor(item).contextEmail, auth.user?.email)
/** Which address mails for this assignment actually go to – the main source of confusion, so always spell it out. */
const status = (item: MyContactAssignmentDto) => item.contextEmail && item.contextEmailVerified
  ? { label: 'Amtsadresse', color: 'success', icon: 'mdi-briefcase-check-outline' }
  : item.contextEmail
    ? { label: 'Bestätigung ausstehend', color: 'warning', icon: 'mdi-email-alert-outline' }
    : { label: 'Persönliche Adresse', color: undefined, icon: 'mdi-account-outline' }

async function save(item: MyContactAssignmentDto): Promise<void> {
  if (draftError(item)) return
  saving.value = item.assignmentId
  try {
    const draft = draftFor(item)
    const address = draft.contextEmail.trim()
    const changed = address.toLowerCase() !== (item.contextEmail ?? '').toLowerCase()
    await api.put(`/api/users/me/contact-assignments/${item.assignmentId}/email`, { contextEmail: address || null, contextEmailVisibility: draft.contextEmailVisibility })
    await assignments.refetch()
    ui.notify(changed && address
      ? `Gespeichert. Bitte bestätige die Amtsadresse über den Link, den wir an ${address} geschickt haben.`
      : changed ? 'Amtsadresse entfernt. Mails zu dieser Zuordnung gehen wieder an deine persönliche Adresse.' : 'Zuordnung wurde gespeichert.', 'success')
  } catch { ui.notify('Amtsadresse konnte nicht gespeichert werden.', 'error') }
  finally { saving.value = '' }
}
async function resendVerification(item: MyContactAssignmentDto): Promise<void> {
  saving.value = item.assignmentId
  try { await api.post(`/api/users/me/contact-assignments/${item.assignmentId}/email/resend-verification`); ui.notify('Bestätigungs-E-Mail wurde erneut versendet.', 'success') }
  catch { ui.notify('Bestätigungs-E-Mail konnte nicht versendet werden.', 'error') }
  finally { saving.value = '' }
}
</script>

<template>
  <p class="text-body-large mb-2">Hier siehst du, wo du als Kontaktperson eingetragen bist. Mails zu einer Zuordnung gehen an deine persönliche Adresse <strong>{{ auth.user?.email }}</strong> – außer du hinterlegst dort eine Amtsadresse und bestätigst sie.</p>
  <p class="text-body-medium text-medium-emphasis mb-4">
    Beispiel: Als Vorstand beim TSV nutzt du vorstand@tsv.de, als Trainer der U17 deine persönliche Adresse. Angemeldet wirst du immer mit deiner persönlichen Adresse.
    <a :href="ROLE_EMAIL_HELP_URL" target="_blank" rel="noopener noreferrer">Mehr erfahren</a>
  </p>
  <v-progress-linear v-if="assignments.isPending.value" indeterminate color="primary" />
  <v-alert v-else-if="assignments.isError.value" type="error">Zuordnungen konnten nicht geladen werden.</v-alert>
  <v-empty-state v-else-if="!assignments.data.value?.length" icon="mdi-account-group-outline" title="Keine Zuordnungen" text="Du bist noch bei keinem Verein, keiner Abteilung und keiner Gruppe als Kontaktperson eingetragen." />
  <v-expansion-panels v-else variant="accordion">
    <v-expansion-panel v-for="item in assignments.data.value" :key="item.assignmentId">
      <v-expansion-panel-title>
        <div class="d-flex align-center ga-3 min-w-0">
          <v-icon :icon="isDepartment(item) ? 'mdi-source-branch' : 'mdi-account-group'" color="primary" />
          <div class="min-w-0">
            <div class="font-weight-medium text-truncate">{{ item.organizationName }}</div>
            <div class="text-body-medium text-medium-emphasis text-truncate">{{ [organizationPath({ clubName: item.parentClubName, parentDepartmentName: item.parentDepartmentName }), item.positionName].filter(Boolean).join(' · ') || 'Kontaktperson' }}</div>
          </div>
        </div>
        <v-chip :color="status(item).color" :prepend-icon="status(item).icon" size="small" variant="tonal" class="ml-auto mr-3 flex-shrink-0">{{ status(item).label }}</v-chip>
      </v-expansion-panel-title>
      <v-expansion-panel-text>
        <v-form @submit.prevent="save(item)">
          <p class="mb-3">
            <v-icon icon="mdi-email-arrow-right-outline" size="small" class="mr-1" />Mails zu dieser Zuordnung gehen an:
            <strong>{{ item.contextEmail && item.contextEmailVerified ? item.contextEmail : auth.user?.email }}</strong>
            <span class="text-medium-emphasis">({{ item.contextEmail && item.contextEmailVerified ? 'Amtsadresse, bestätigt' : 'persönliche Adresse' }})</span>
          </p>
          <v-alert v-if="item.contextEmail && !item.contextEmailVerified" type="warning" variant="tonal" density="compact" class="mb-4">
            Die Amtsadresse <strong>{{ item.contextEmail }}</strong> ist noch nicht bestätigt. Bis dahin gehen Mails an deine persönliche Adresse. Den Bestätigungslink findest du im Postfach von {{ item.contextEmail }}.
          </v-alert>
          <v-row dense>
            <v-col cols="12" sm="7"><v-text-field v-model="draftFor(item).contextEmail" label="Amts- oder Vereinsadresse (optional)" placeholder="z. B. vorstand@verein.de" type="email" prepend-inner-icon="mdi-briefcase-outline" hint="Leer lassen, um deine persönliche Adresse zu nutzen. Eine neue Adresse muss per Link bestätigt werden." persistent-hint :error-messages="draftError(item)" /></v-col>
            <v-col cols="12" sm="5"><v-select v-model="draftFor(item).contextEmailVisibility" label="Amtsadresse sichtbar für" :items="visibilityOptions" :disabled="!draftFor(item).contextEmail.trim()" /></v-col>
          </v-row>
          <p v-if="item.contextEmail && item.contextEmailVerified" class="text-body-medium text-medium-emphasis mt-2 mb-3">
            <v-icon icon="mdi-account-switch-outline" size="small" class="mr-1" />Gibst du das Amt ab? Dann entferne die Amtsadresse hier. Die Nachfolge trägt sie in ihrer eigenen Zuordnung ein und bestätigt sie.
          </p>
          <div class="d-flex flex-wrap ga-2">
            <v-btn variant="text" :to="link(item)" prepend-icon="mdi-open-in-app">Zur Organisation</v-btn>
            <v-spacer />
            <v-btn v-if="item.contextEmail && !item.contextEmailVerified" variant="outlined" :loading="saving === item.assignmentId" @click="resendVerification(item)">Bestätigung erneut senden</v-btn>
            <v-btn type="submit" color="primary" variant="flat" :loading="saving === item.assignmentId" :disabled="!!draftError(item)">Speichern</v-btn>
          </div>
        </v-form>
      </v-expansion-panel-text>
    </v-expansion-panel>
  </v-expansion-panels>
</template>
