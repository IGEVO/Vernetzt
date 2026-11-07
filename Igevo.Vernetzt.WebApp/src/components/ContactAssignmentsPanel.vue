<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import AvatarImage from '@/components/AvatarImage.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import InviteEmailFields from '@/components/InviteEmailFields.vue'
import { api, ApiError, toQuery } from '@/services/api'
import { initialsOf } from '@/services/avatars'
import { sendInvitation } from '@/services/invitations'
import { contextEmailError, EMAIL_PATTERN, ROLE_EMAIL_HELP_URL } from '@/services/roleEmail'
import { useUiStore } from '@/stores/ui'
import type { ContactAssignmentDto, ContactDto, NamedEntityDto } from '@/types/api'

const props = defineProps<{ entityType: 'clubs' | 'departments'; entityId: string; assignments: ContactAssignmentDto[]; canManage: boolean; isGroup?: boolean }>()
const emit = defineEmits<{ changed: [] }>()
const ui = useUiStore()
const dialog = ref(false); const saving = ref(false); const error = ref('')
const mode = ref<'existing' | 'invite'>('existing')
const editing = ref<ContactAssignmentDto | null>(null)
const deleting = ref<ContactAssignmentDto | null>(null)
const form = reactive({ userId: '', email: '', contextEmail: '', positionId: '' as string | null, canEdit: false, notes: '' })
const emailValid = computed(() => EMAIL_PATTERN.test(form.email.trim()) && !contextEmailError(form.contextEmail, form.email))
const noun = computed(() => props.entityType === 'clubs' ? 'den Verein' : props.isGroup ? 'die Gruppe' : 'die Abteilung')
// /api/users/contacts is available to everyone who may manage contacts (/api/users is restricted to admins).
const people = useQuery({ queryKey: ['users', 'contacts', 'picker'], queryFn: ({ signal }) => api.get<ContactDto[]>('/api/users/contacts', signal), enabled: computed(() => dialog.value && !editing.value) })
const positions = useQuery({ queryKey: ['contact-positions', 'active'], queryFn: ({ signal }) => api.get<NamedEntityDto[]>('/api/contactpositions/active', signal), enabled: computed(() => dialog.value) })
const assignedIds = computed(() => new Set(props.assignments.map((item) => item.userId)))
const personItems = computed(() => (people.data.value ?? [])
  .map((item) => ({ title: `${item.firstName} ${item.lastName}`, subtitle: item.email ?? undefined, value: item.id, props: { disabled: assignedIds.value.has(item.id) } }))
  .sort((a, b) => a.title.localeCompare(b.title, 'de')))
const canSubmit = computed(() => editing.value || (mode.value === 'existing' ? !!form.userId : emailValid.value))
const submitLabel = computed(() => editing.value ? 'Speichern' : mode.value === 'invite' ? 'Einladen und zuordnen' : 'Zuordnen')

function open(item?: ContactAssignmentDto): void {
  editing.value = item ?? null
  mode.value = 'existing'; error.value = ''
  Object.assign(form, item ? { userId: item.userId, email: '', contextEmail: '', positionId: item.contactPositionId ?? null, canEdit: item.canEdit, notes: item.assignmentDetails ?? '' } : { userId: '', email: '', contextEmail: '', positionId: null, canEdit: false, notes: '' })
  dialog.value = true
}
async function save(): Promise<void> {
  if (!canSubmit.value) return
  saving.value = true; error.value = ''
  try {
    if (!editing.value && mode.value === 'invite') {
      const email = form.email.trim()
      await sendInvitation({ email, clubId: props.entityType === 'clubs' ? props.entityId : null, departmentId: props.entityType === 'departments' ? props.entityId : null, contactPositionId: form.positionId || null, canEdit: form.canEdit, assignmentDetails: form.notes.trim() || null, contextEmail: form.contextEmail.trim() || null })
      dialog.value = false
      ui.notify(`Einladung an ${email} wurde versendet. Die Zuordnung entsteht, sobald die Person die Einladung annimmt.`, 'success')
      return
    }
    const path = `/api/${props.entityType}/${props.entityId}/contact-persons/${form.userId}${toQuery({ positionId: form.positionId || null, canEdit: form.canEdit })}`
    if (editing.value) await api.put(path, { notes: form.notes || null })
    else await api.post(path, { notes: form.notes || null })
    dialog.value = false; emit('changed'); ui.notify('Kontaktzuordnung wurde gespeichert.', 'success')
  } catch (reason) { error.value = reason instanceof ApiError ? reason.message : 'Kontaktzuordnung konnte nicht gespeichert werden.' }
  finally { saving.value = false }
}
/** A role address stays with the office: remind to pass it on to the successor's own assignment. */
const deleteText = computed(() => {
  const roleAddress = deleting.value?.contextEmailVerified ? deleting.value.contextEmail : null
  return roleAddress
    ? `Die Person verliert ihre Zuordnung zu dieser Organisation. Die Amtsadresse ${roleAddress} war hier hinterlegt – die Nachfolge kann sie in ihrer eigenen Zuordnung unter „Mein Konto → Zuordnungen“ eintragen und bestätigen.`
    : 'Die Person verliert ihre Zuordnung zu dieser Organisation.'
})
async function remove(): Promise<void> {
  if (!deleting.value) return
  try { await api.delete(`/api/${props.entityType}/${props.entityId}/contact-persons/${deleting.value.userId}`); deleting.value = null; emit('changed'); ui.notify('Kontaktzuordnung wurde entfernt.', 'success') }
  catch { ui.notify('Kontaktzuordnung konnte nicht entfernt werden.', 'error') }
}
</script>

<template>
  <div class="d-flex align-center mb-3">
    <p class="text-body-large text-medium-emphasis mb-0">{{ assignments.length ? `${assignments.length} ${assignments.length === 1 ? 'Kontaktperson' : 'Kontaktpersonen'}` : 'Keine Kontaktpersonen eingetragen.' }}</p>
    <v-spacer />
    <v-btn v-if="canManage" color="primary" prepend-icon="mdi-account-plus" @click="open()">Kontaktperson hinzufügen</v-btn>
  </div>
  <v-card v-if="assignments.length">
    <v-list lines="two">
      <v-list-item v-for="contact in assignments" :key="contact.assignmentId" :title="`${contact.firstName ?? ''} ${contact.lastName ?? ''}`.trim()" :subtitle="[contact.positionName, contact.assignmentDetails].filter(Boolean).join(' · ') || undefined">
        <template #prepend><AvatarImage type="users" :id="contact.userId" :has-avatar="contact.hasAvatar" :initials="initialsOf(contact.firstName, contact.lastName)" class="mr-4" /></template>
        <div class="d-flex flex-wrap gc-4 mt-1 text-body-medium">
          <a v-if="contact.contextEmail || contact.email" :href="`mailto:${contact.contextEmail || contact.email}`">{{ contact.contextEmail || contact.email }}</a>
          <a v-if="contact.phoneNumber" :href="`tel:${contact.phoneNumber}`">{{ contact.phoneNumber }}</a>
        </div>
        <template #append>
          <v-chip v-if="contact.contextEmail && contact.contextEmailVerified" size="small" variant="tonal" color="secondary" prepend-icon="mdi-briefcase-outline" class="mr-2 d-none d-sm-flex" title="Mails gehen an die Amtsadresse dieser Zuordnung">Amtsadresse</v-chip>
          <v-chip v-if="contact.canEdit" size="small" variant="tonal" prepend-icon="mdi-pencil-lock" class="mr-2 d-none d-sm-flex">Darf bearbeiten</v-chip>
          <template v-if="canManage">
            <v-btn icon="mdi-pencil" variant="text" size="small" aria-label="Zuordnung bearbeiten" title="Zuordnung bearbeiten" @click="open(contact)" />
            <v-btn icon="mdi-delete" variant="text" size="small" color="error" aria-label="Zuordnung entfernen" title="Zuordnung entfernen" @click="deleting = contact" />
          </template>
        </template>
      </v-list-item>
    </v-list>
  </v-card>
  <v-dialog v-model="dialog" max-width="620" scrollable>
    <v-card :title="editing ? 'Kontaktzuordnung bearbeiten' : 'Kontaktperson hinzufügen'">
      <v-form @submit.prevent="save">
        <v-card-text>
          <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = ''">{{ error }}</v-alert>
          <template v-if="editing">
            <div class="d-flex align-center ga-3 mb-4">
              <AvatarImage type="users" :id="editing.userId" :has-avatar="editing.hasAvatar" :initials="initialsOf(editing.firstName, editing.lastName)" />
              <div class="text-title-medium">{{ editing.firstName }} {{ editing.lastName }}</div>
            </div>
          </template>
          <template v-else>
            <v-tabs v-model="mode" color="primary" class="tab-bar mb-4" grow>
              <v-tab value="existing" prepend-icon="mdi-account-search">Bestehende Person</v-tab>
              <v-tab value="invite" prepend-icon="mdi-email-plus">Neu einladen</v-tab>
            </v-tabs>
            <v-autocomplete v-if="mode === 'existing'" v-model="form.userId" :items="personItems" label="Person suchen *" placeholder="Name oder E-Mail" :loading="people.isFetching.value" no-data-text="Keine passende Person gefunden – lade sie über „Neu einladen“ ein." hint="Bereits zugeordnete Personen sind ausgegraut." persistent-hint class="mb-2">
              <template #item="{ props: itemProps, item }"><v-list-item v-bind="itemProps" :subtitle="item.subtitle" /></template>
            </v-autocomplete>
            <template v-else>
              <v-alert type="info" variant="tonal" density="compact" class="mb-4">
                Bitte zuerst unter „Bestehende Person“ prüfen, ob die Person schon registriert ist. Die Person wird nach der Registrierung automatisch als Kontaktperson zugeordnet.
                Für die Anmeldung gilt die <strong>persönliche</strong> Adresse – eine Vereins- oder Amtsadresse wie vorstand@verein.de gehört ins Feld darunter.
                <a :href="ROLE_EMAIL_HELP_URL" target="_blank" rel="noopener noreferrer">Mehr erfahren</a>
              </v-alert>
              <InviteEmailFields v-model:email="form.email" v-model:context-email="form.contextEmail" with-assignment />
            </template>
          </template>
          <v-select v-model="form.positionId" :items="positions.data.value ?? []" item-title="name" item-value="id" label="Position (optional)" clearable class="mt-2" />
          <v-text-field v-model="form.notes" label="Zuordnungsdetails (optional)" hint="z. B. 1. Mannschaft, U17, Ansprechperson Sponsoring" persistent-hint maxlength="500" class="mb-2" />
          <v-switch v-model="form.canEdit" color="primary" :label="`Darf ${noun} bearbeiten`" hide-details />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn :disabled="saving" @click="dialog = false">Abbrechen</v-btn>
          <v-btn type="submit" color="primary" variant="flat" :loading="saving" :disabled="!canSubmit" :prepend-icon="!editing && mode === 'invite' ? 'mdi-send' : undefined">{{ submitLabel }}</v-btn>
        </v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>
  <ConfirmDialog :model-value="!!deleting" title="Kontaktzuordnung entfernen?" :text="deleteText" @update:model-value="!$event && (deleting = null)" @confirm="remove" />
</template>
