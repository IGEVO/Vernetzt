<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { api, ApiError } from '@/services/api'
import InviteEmailFields from '@/components/InviteEmailFields.vue'
import { sendInvitation } from '@/services/invitations'
import { organizationPath } from '@/services/organizationPath'
import { contextEmailError, EMAIL_PATTERN, ROLE_EMAIL_HELP_URL } from '@/services/roleEmail'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { ClubDto, DepartmentDto, NamedEntityDto } from '@/types/api'

/** Invite a new person by e-mail, optionally with a pre-linked club / department assignment and roles. */
const open = defineModel<boolean>({ required: true })
const emit = defineEmits<{ invited: [email: string] }>()
const auth = useAuthStore(); const ui = useUiStore()
const busy = ref(false); const error = ref('')
const form = reactive({ email: '', contextEmail: '', assignment: 'none' as 'none' | 'club' | 'department', clubId: null as string | null, departmentId: null as string | null, positionId: null as string | null, details: '', canEdit: false, claims: [] as number[] })
const isAdmin = computed(() => auth.hasRole('SystemAdmin', 'Manager'))
const claimOptions = computed(() => [
  ...(auth.hasRole('SystemAdmin') ? [{ value: 0, title: 'Systemadministrator', text: 'Voller Zugriff inkl. Systemeinstellungen' }] : []),
  { value: 1, title: 'Manager', text: 'Verwaltet Vereine, Stammdaten und Benutzer' },
  { value: 2, title: 'Leserechte', text: 'Darf alle Daten lesen' }
])
const emailValid = computed(() => EMAIL_PATTERN.test(form.email.trim()) && (form.assignment === 'none' || !contextEmailError(form.contextEmail, form.email)))
const clubs = useQuery({ queryKey: ['clubs', 'all'], queryFn: ({ signal }) => api.get<ClubDto[]>('/api/clubs', signal), enabled: computed(() => open.value && form.assignment !== 'none') })
const departments = useQuery({ queryKey: computed(() => ['departments', 'by-club', form.clubId]), queryFn: ({ signal }) => api.get<DepartmentDto[]>(`/api/departments?clubId=${form.clubId}&includeGroups=true`, signal), enabled: computed(() => open.value && form.assignment === 'department' && !!form.clubId) })
const positions = useQuery({ queryKey: ['contact-positions', 'active'], queryFn: ({ signal }) => api.get<NamedEntityDto[]>('/api/contactpositions/active', signal), enabled: computed(() => open.value && form.assignment !== 'none') })
const sortedClubs = computed(() => [...(clubs.data.value ?? [])].sort((a, b) => a.name.localeCompare(b.name, 'de')))
/** The API lists each department followed by its groups; groups are indented and name their department. */
function departmentTitle(item: DepartmentDto): string {
  return item.isGroup ? organizationPath({ parentDepartmentName: item.parentDepartmentName, departmentName: item.name }) : item.name
}
function departmentItemProps(item: DepartmentDto): Record<string, unknown> {
  return item.isGroup
    ? { subtitle: 'Gruppe', class: 'pl-8', prependIcon: 'mdi-account-group-outline' }
    : { subtitle: 'Abteilung', prependIcon: 'mdi-source-branch' }
}
const assignmentValid = computed(() => form.assignment === 'none' || (form.assignment === 'club' ? !!form.clubId : !!form.departmentId))

watch(open, (value) => { if (value) { Object.assign(form, { email: '', contextEmail: '', assignment: 'none', clubId: null, departmentId: null, positionId: null, details: '', canEdit: false, claims: [] }); error.value = '' } })
watch(() => form.clubId, () => { form.departmentId = null })

async function submit(): Promise<void> {
  if (!emailValid.value || !assignmentValid.value) return
  busy.value = true; error.value = ''
  try {
    const email = form.email.trim()
    await sendInvitation({
      email,
      clubId: form.assignment === 'club' ? form.clubId : null,
      departmentId: form.assignment === 'department' ? form.departmentId : null,
      contactPositionId: form.assignment !== 'none' ? form.positionId : null,
      canEdit: form.assignment !== 'none' && form.canEdit,
      assignmentDetails: form.assignment !== 'none' ? form.details.trim() || null : null,
      contextEmail: form.assignment !== 'none' ? form.contextEmail.trim() || null : null,
      pendingClaims: form.claims
    })
    open.value = false
    ui.notify(`Einladung an ${email} wurde versendet.`, 'success')
    emit('invited', email)
  } catch (reason) { error.value = reason instanceof ApiError ? reason.message : 'Die Einladung konnte nicht versendet werden.' }
  finally { busy.value = false }
}
</script>

<template>
  <v-dialog v-model="open" max-width="640" scrollable>
    <v-card title="Person einladen">
      <v-form @submit.prevent="submit">
        <v-card-text>
          <p class="mb-3">Die Person erhält eine E-Mail mit einem Link zur Registrierung.</p>
          <v-alert type="info" variant="tonal" density="compact" class="mb-4">
            Bitte vorher prüfen, ob die Person schon registriert ist. Für die Anmeldung gilt die <strong>persönliche</strong> Adresse – eine Vereins- oder Amtsadresse wie vorstand@verein.de kann bei einer Zuordnung zusätzlich angegeben werden.
            <a :href="ROLE_EMAIL_HELP_URL" target="_blank" rel="noopener noreferrer">Mehr erfahren</a>
          </v-alert>
          <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = ''">{{ error }}</v-alert>
          <InviteEmailFields v-model:email="form.email" v-model:context-email="form.contextEmail" :with-assignment="form.assignment !== 'none'" part="login" />

          <div class="text-title-small mt-2 mb-1">Als Kontaktperson zuordnen (optional)</div>
          <v-btn-toggle v-model="form.assignment" mandatory color="primary" variant="outlined" density="comfortable" divided class="mb-4">
            <v-btn value="none">Keine</v-btn>
            <v-btn value="club">Verein</v-btn>
            <v-btn value="department">Abteilung oder Gruppe</v-btn>
          </v-btn-toggle>
          <template v-if="form.assignment !== 'none'">
            <v-autocomplete v-model="form.clubId" :items="sortedClubs" item-title="name" item-value="id" label="Verein *" :loading="clubs.isFetching.value" />
            <v-autocomplete v-if="form.assignment === 'department'" v-model="form.departmentId" :items="departments.data.value ?? []" :item-title="departmentTitle" item-value="id" :item-props="departmentItemProps" label="Abteilung oder Gruppe *" :disabled="!form.clubId" :loading="departments.isFetching.value" :no-data-text="form.clubId ? 'Dieser Verein hat keine Abteilungen' : 'Bitte zuerst einen Verein wählen'" />
            <v-select v-model="form.positionId" :items="positions.data.value ?? []" item-title="name" item-value="id" label="Position (optional)" clearable />
            <InviteEmailFields v-model:email="form.email" v-model:context-email="form.contextEmail" with-assignment part="context" />
            <v-text-field v-model="form.details" label="Zuordnungsdetails (optional)" hint="z. B. 1. Mannschaft, U17, Ansprechperson Sponsoring" persistent-hint maxlength="500" />
            <v-switch v-model="form.canEdit" color="primary" label="Darf diese Organisation bearbeiten" hide-details class="mb-2" />
          </template>

          <template v-if="isAdmin">
            <div class="text-title-small mt-4 mb-1">Rollen (optional)</div>
            <p class="text-body-medium text-medium-emphasis mb-1">Werden nach der Registrierung automatisch aktiv.</p>
            <v-checkbox v-for="claim in claimOptions" :key="claim.value" v-model="form.claims" :value="claim.value" :label="`${claim.title} – ${claim.text}`" density="compact" hide-details />
          </template>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn :disabled="busy" @click="open = false">Abbrechen</v-btn>
          <v-btn type="submit" color="primary" variant="flat" prepend-icon="mdi-send" :loading="busy" :disabled="!emailValid || !assignmentValid">Einladung senden</v-btn>
        </v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>
</template>
