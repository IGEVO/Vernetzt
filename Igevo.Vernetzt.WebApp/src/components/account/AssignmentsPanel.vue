<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { api, ApiError } from '@/services/api'
import { contextEmailError, ROLE_EMAIL_HELP_URL } from '@/services/roleEmail'
import { isStricterVisibility, restrictContextEmailVisibility, visibilityDisplay, visibilityOptions, visibilityOptionsUpTo } from '@/services/visibility'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { MyContactAssignmentDto, VisibilityLevel } from '@/types/api'
import { organizationPath, positionLabel } from '@/services/organizationPath'

const ui = useUiStore(); const auth = useAuthStore(); const route = useRoute(); const queryClient = useQueryClient(); const saving = ref('')
const assignments = useQuery({ queryKey: ['user', 'me', 'assignments'], queryFn: ({ signal }) => api.get<MyContactAssignmentDto[]>('/api/users/me/contact-assignments', signal) })
// Editable drafts per assignment: query results are read-only and must not be bound with v-model directly.
// A pending assignment has no preselected visibility: the user has to choose before consenting (privacy by default).
// The role address has its own visibility; null means "same as the assignment" and it never exceeds the assignment.
interface Draft { contextEmail: string; visibility: VisibilityLevel | null; contextEmailVisibility: VisibilityLevel | null }
const initialDraft = (item: MyContactAssignmentDto): Draft => ({
  contextEmail: item.contextEmail ?? '',
  visibility: item.isConfirmed ? item.visibility : null,
  contextEmailVisibility: item.isConfirmed ? item.contextEmailVisibility ?? item.visibility : null
})
const drafts = reactive<Record<string, Draft>>({})
/** Open panel; a link from the consent mail (?zuordnung=<id>) opens that assignment. */
const expanded = ref<string | undefined>(typeof route.query.zuordnung === 'string' ? route.query.zuordnung : undefined)
watch(() => assignments.data.value, (items) => {
  for (const item of items ?? []) drafts[item.assignmentId] = initialDraft(item)
  if (!expanded.value) expanded.value = items?.find((item) => !item.isConfirmed)?.assignmentId
}, { immediate: true })
const draftFor = (item: MyContactAssignmentDto) => (drafts[item.assignmentId] ??= initialDraft(item))
/** Effective role address visibility of a draft (follows the assignment until chosen separately). */
const emailVisibilityOf = (draft: Draft) => draft.contextEmailVisibility ?? draft.visibility
function setVisibility(item: MyContactAssignmentDto, value: VisibilityLevel): void {
  const draft = draftFor(item)
  draft.visibility = value
  // A stricter assignment also restricts its role address
  draft.contextEmailVisibility = restrictContextEmailVisibility(value, draft.contextEmailVisibility)
}
/** Shown when the role address is less visible than the assignment itself. */
const showsEmailVisibility = (item: MyContactAssignmentDto) => !!item.contextEmail && item.contextEmailVisibility !== undefined
  && isStricterVisibility(item.contextEmailVisibility, item.visibility)
const declining = ref<MyContactAssignmentDto | null>(null)
const isDepartment = (item: MyContactAssignmentDto) => item.organizationType === 1 || item.organizationType === 'Department'
const link = (item: MyContactAssignmentDto) => `/${isDepartment(item) ? 'departments' : 'clubs'}/${item.organizationId}`
const draftError = (item: MyContactAssignmentDto) => contextEmailError(draftFor(item).contextEmail, auth.user?.email)
/** Which address mails for this assignment actually go to – the main source of confusion, so always spell it out. */
const status = (item: MyContactAssignmentDto) => !item.isConfirmed
  ? { label: 'Zustimmung ausstehend', color: 'warning', icon: 'mdi-account-clock-outline' }
  : item.contextEmail && item.contextEmailVerified
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
    await api.put(`/api/users/me/contact-assignments/${item.assignmentId}/email`, { contextEmail: address || null, visibility: draft.visibility, contextEmailVisibility: emailVisibilityOf(draft) })
    await assignments.refetch()
    ui.notify(changed && address
      ? `Gespeichert. Bitte bestätige die Amtsadresse über den Link, den wir an ${address} geschickt haben.`
      : changed ? 'Amtsadresse entfernt. Mails zu dieser Zuordnung gehen wieder an deine persönliche Adresse.' : 'Zuordnung wurde gespeichert.', 'success')
  } catch (reason) { ui.notify(reason instanceof ApiError && reason.message ? reason.message : 'Zuordnung konnte nicht gespeichert werden.', 'error') }
  finally { saving.value = '' }
}
async function afterConsentChange(): Promise<void> {
  await assignments.refetch()
  // Rights, dashboard and organization pages depend on the consent
  void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
}
async function confirm(item: MyContactAssignmentDto): Promise<void> {
  if (draftFor(item).visibility === null) return
  saving.value = item.assignmentId
  try {
    await api.post(`/api/users/me/contact-assignments/${item.assignmentId}/confirm`, { visibility: draftFor(item).visibility, contextEmailVisibility: emailVisibilityOf(draftFor(item)) })
    await afterConsentChange()
    ui.notify('Danke! Die Zuordnung ist jetzt aktiv.', 'success')
  } catch (reason) { ui.notify(reason instanceof ApiError && reason.message ? reason.message : 'Zustimmung konnte nicht gespeichert werden.', 'error') }
  finally { saving.value = '' }
}
async function decline(): Promise<void> {
  const item = declining.value
  if (!item) return
  declining.value = null
  saving.value = item.assignmentId
  try {
    await api.post(`/api/users/me/contact-assignments/${item.assignmentId}/decline`)
    await afterConsentChange()
    ui.notify('Zuordnung wurde abgelehnt und entfernt.', 'success')
  } catch (reason) { ui.notify(reason instanceof ApiError && reason.message ? reason.message : 'Zuordnung konnte nicht abgelehnt werden.', 'error') }
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
  <v-expansion-panels v-else v-model="expanded" variant="accordion">
    <v-expansion-panel v-for="item in assignments.data.value" :key="item.assignmentId" :value="item.assignmentId">
      <v-expansion-panel-title>
        <div class="d-flex align-center ga-3 min-w-0">
          <v-icon :icon="isDepartment(item) ? 'mdi-source-branch' : 'mdi-account-group'" color="primary" />
          <div class="min-w-0">
            <div class="font-weight-medium text-truncate">{{ item.organizationName }}</div>
            <div class="text-body-medium text-medium-emphasis text-truncate">{{ [organizationPath({ clubName: item.parentClubName, parentDepartmentName: item.parentDepartmentName }), positionLabel(item)].filter(Boolean).join(' · ') || 'Kontaktperson' }}</div>
          </div>
        </div>
        <div class="d-flex flex-wrap justify-end ga-2 ml-auto mr-3 flex-shrink-0">
          <v-chip v-if="item.isConfirmed" :color="visibilityDisplay(item.visibility).color" :prepend-icon="visibilityDisplay(item.visibility).icon" size="small" variant="tonal" :title="`Zuordnung sichtbar für: ${visibilityDisplay(item.visibility).title}`">{{ visibilityDisplay(item.visibility).title }}</v-chip>
          <v-chip v-if="item.isConfirmed && showsEmailVisibility(item)" :color="visibilityDisplay(item.contextEmailVisibility!).color" prepend-icon="mdi-briefcase-outline" size="small" variant="tonal" :title="`Amtsadresse sichtbar für: ${visibilityDisplay(item.contextEmailVisibility!).title}`">Amtsadresse: {{ visibilityDisplay(item.contextEmailVisibility!).title }}</v-chip>
          <v-chip :color="status(item).color" :prepend-icon="status(item).icon" size="small" variant="tonal">{{ status(item).label }}</v-chip>
        </div>
      </v-expansion-panel-title>
      <v-expansion-panel-text>
        <v-form v-if="!item.isConfirmed" @submit.prevent="confirm(item)">
          <v-alert type="warning" variant="tonal" density="compact" class="mb-4">
            Du wurdest hier als Kontaktperson eingetragen. Bitte stimme zu und lege fest, wer die Zuordnung sehen darf.
            Bis dahin sehen dich hier nur die Verwaltung und die Personen, die die Organisation bearbeiten. Rechte und Mails zu dieser Zuordnung sind noch nicht aktiv.
          </v-alert>
          <v-select :model-value="draftFor(item).visibility" label="Zuordnung sichtbar für *" :items="visibilityOptions" item-props hint="Bitte wähle aus, wer die Zuordnung sehen darf. „Öffentlich“ heißt: auf der öffentlichen Vereinsseite. Deine persönliche E-Mail und Telefonnummer folgen weiter deinen Profileinstellungen. Du kannst das jederzeit hier ändern." persistent-hint class="mb-3" @update:model-value="setVisibility(item, $event)" />
          <v-select v-if="item.contextEmail" v-model="draftFor(item).contextEmailVisibility" :label="`Amtsadresse ${item.contextEmail} sichtbar für`" :items="visibilityOptionsUpTo(draftFor(item).visibility)" item-props :disabled="draftFor(item).visibility === null" placeholder="wie die Zuordnung" persistent-placeholder hint="Ohne eigene Auswahl gilt dieselbe Sichtbarkeit wie für die Zuordnung. Die Amtsadresse ist nie sichtbarer als die Zuordnung." persistent-hint class="mb-3" />
          <div class="d-flex flex-wrap ga-2">
            <v-btn variant="text" :to="link(item)" prepend-icon="mdi-open-in-app">Zur Organisation</v-btn>
            <v-spacer />
            <v-btn variant="outlined" color="error" :disabled="saving === item.assignmentId" @click="declining = item">Ablehnen</v-btn>
            <v-btn type="submit" color="primary" variant="flat" prepend-icon="mdi-check" :disabled="draftFor(item).visibility === null" :loading="saving === item.assignmentId">Zustimmen</v-btn>
          </div>
        </v-form>
        <v-form v-else @submit.prevent="save(item)">
          <p class="mb-3">
            <v-icon icon="mdi-email-arrow-right-outline" size="small" class="mr-1" />Mails zu dieser Zuordnung gehen an:
            <strong>{{ item.contextEmail && item.contextEmailVerified ? item.contextEmail : auth.user?.email }}</strong>
            <span class="text-medium-emphasis">({{ item.contextEmail && item.contextEmailVerified ? 'Amtsadresse, bestätigt' : 'persönliche Adresse' }})</span>
          </p>
          <v-alert v-if="item.contextEmail && !item.contextEmailVerified" type="warning" variant="tonal" density="compact" class="mb-4">
            Die Amtsadresse <strong>{{ item.contextEmail }}</strong> ist noch nicht bestätigt. Bis dahin gehen Mails an deine persönliche Adresse. Den Bestätigungslink findest du im Postfach von {{ item.contextEmail }}.
          </v-alert>
          <v-row dense>
            <v-col cols="12"><v-text-field v-model="draftFor(item).contextEmail" label="Amts- oder Vereinsadresse (optional)" placeholder="z. B. vorstand@verein.de" type="email" prepend-inner-icon="mdi-briefcase-outline" hint="Leer lassen, um deine persönliche Adresse zu nutzen. Eine neue Adresse muss per Link bestätigt werden." persistent-hint :error-messages="draftError(item)" /></v-col>
            <v-col cols="12" sm="6"><v-select :model-value="draftFor(item).visibility" label="Zuordnung sichtbar für" :items="visibilityOptions" item-props @update:model-value="setVisibility(item, $event)" /></v-col>
            <v-col cols="12" sm="6"><v-select v-model="draftFor(item).contextEmailVisibility" label="Amtsadresse sichtbar für" :items="visibilityOptionsUpTo(draftFor(item).visibility)" item-props :disabled="!draftFor(item).contextEmail.trim()" hint="Nie sichtbarer als die Zuordnung" persistent-hint /></v-col>
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
  <ConfirmDialog :model-value="!!declining" title="Zuordnung ablehnen?" :text="`Die Zuordnung zu ${declining?.organizationName ?? ''} wird entfernt. Die Person, die dich eingetragen hat, wird per E-Mail informiert.`" confirm-text="Ablehnen" @update:model-value="!$event && (declining = null)" @confirm="decline" />
</template>
