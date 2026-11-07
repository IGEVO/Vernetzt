<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import AvatarImage from '@/components/AvatarImage.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import InviteUserDialog from '@/components/InviteUserDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import { api } from '@/services/api'
import { initialsOf } from '@/services/avatars'
import { formatDateTime } from '@/services/dates'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { UserDto } from '@/types/api'

const ui = useUiStore(); const auth = useAuthStore()
const search = ref(''); const dialog = ref(false); const inviteDialog = ref(false); const saving = ref(false)
const deleting = ref<UserDto | null>(null); const editing = ref<UserDto | null>(null); const resettingPasskeys = ref<UserDto | null>(null)
const form = reactive({ email: '', firstName: '', lastName: '', phoneNumber: '', emailVisibility: 1, phoneVisibility: 1 })
const changingClaim = ref<number | null>(null)
const claimOptions = [{ value: 0, title: 'Systemadministrator' }, { value: 1, title: 'Manager' }, { value: 2, title: 'Leserechte' }]
const roleLabel = (claim: string | number) => ({ 0: 'Systemadmin', 1: 'Manager', 2: 'Leserechte', SystemAdmin: 'Systemadmin', Manager: 'Manager', DataReader: 'Leserechte' } as Record<string, string>)[String(claim)] ?? String(claim)
const headers = [
  { title: 'Name', key: 'name', sortable: true, value: (item: UserDto) => `${item.lastName} ${item.firstName}` },
  { title: 'E-Mail', key: 'email' },
  { title: 'Rollen', key: 'claims', sortable: false },
  { title: 'Passkey', key: 'passkeyCount', align: 'center' as const },
  { title: 'Letzte Anmeldung', key: 'lastLoginAt' },
  { title: '', key: 'actions', sortable: false, align: 'end' as const }
]

const query = useQuery({ queryKey: ['admin', 'users'], queryFn: ({ signal }) => api.get<UserDto[]>('/api/users', signal) })
const users = computed(() => {
  const term = (search.value ?? '').toLocaleLowerCase('de')
  return (query.data.value ?? []).filter((user) => `${user.firstName} ${user.lastName} ${user.email}`.toLocaleLowerCase('de').includes(term))
})

function open(user?: UserDto): void {
  editing.value = user ?? null
  Object.assign(form, user
    ? { email: user.email, firstName: user.firstName, lastName: user.lastName, phoneNumber: user.phoneNumber ?? '', emailVisibility: user.emailVisibility, phoneVisibility: user.phoneVisibility }
    : { email: '', firstName: '', lastName: '', phoneNumber: '', emailVisibility: 1, phoneVisibility: 1 })
  dialog.value = true
}
async function save(): Promise<void> {
  if (!form.email || !form.firstName.trim() || !form.lastName.trim()) return
  saving.value = true
  try {
    if (editing.value) await api.put(`/api/users/${editing.value.id}`, form); else await api.post('/api/users', form)
    dialog.value = false; await query.refetch(); ui.notify('Benutzer wurde gespeichert.', 'success')
  } catch { ui.notify('Benutzer konnte nicht gespeichert werden.', 'error') }
  finally { saving.value = false }
}
async function remove(): Promise<void> {
  if (!deleting.value) return
  try { await api.delete(`/api/users/${deleting.value.id}`); deleting.value = null; await query.refetch(); ui.notify('Benutzer wurde gelöscht.', 'success') }
  catch { ui.notify('Benutzer konnte nicht gelöscht werden.', 'error') }
}
// Only system administrators may edit or delete other system administrators.
const isSystemAdmin = (user: UserDto): boolean => user.claims.some((claim) => roleLabel(claim) === 'Systemadmin')
const canManage = (user: UserDto): boolean => auth.hasRole('SystemAdmin') || user.id === auth.user?.id || !isSystemAdmin(user)
const canDelete = (user: UserDto): boolean => auth.hasRole('SystemAdmin') || !isSystemAdmin(user)
// Only system administrators may reset passkeys of other users, and only if there is something to reset.
const canResetPasskeys = (user: UserDto): boolean => auth.hasRole('SystemAdmin') && user.id !== auth.user?.id && (user.passkeyCount ?? 0) > 0
async function resetPasskeys(): Promise<void> {
  const user = resettingPasskeys.value
  if (!user) return
  try { await api.delete(`/api/users/${user.id}/passkeys`); await query.refetch(); ui.notify(`Passkeys von ${user.firstName} ${user.lastName} wurden zurückgesetzt.`, 'success') }
  catch { ui.notify('Passkeys konnten nicht zurückgesetzt werden.', 'error') }
  finally { resettingPasskeys.value = null }
}
function hasClaim(value: number): boolean { return editing.value?.claims.some((claim) => Number(claim) === value || roleLabel(claim) === roleLabel(value)) ?? false }
async function toggleClaim(value: number): Promise<void> {
  if (!editing.value) return
  changingClaim.value = value
  try {
    if (hasClaim(value)) await api.delete(`/api/users/${editing.value.id}/claims/${value}`)
    else await api.post(`/api/users/${editing.value.id}/claims`, { claim: value })
    await query.refetch()
    editing.value = query.data.value?.find((item) => item.id === editing.value?.id) ?? editing.value
    ui.notify('Rollen wurden aktualisiert.', 'success')
  } catch { ui.notify('Rolle konnte nicht geändert werden.', 'error') }
  finally { changingClaim.value = null }
}
</script>

<template>
  <PageHeader title="Benutzerverwaltung">
    <template #actions>
      <v-btn variant="tonal" prepend-icon="mdi-plus" @click="open()">Anlegen</v-btn>
      <v-btn color="primary" prepend-icon="mdi-email-plus" @click="inviteDialog = true">Person einladen</v-btn>
    </template>
  </PageHeader>
  <v-text-field v-model="search" label="Benutzer suchen" placeholder="Name oder E-Mail" prepend-inner-icon="mdi-magnify" clearable hide-details class="mb-6" />
  <v-progress-linear v-if="query.isPending.value" indeterminate color="primary" />
  <v-alert v-else-if="query.isError.value" type="error">Benutzer konnten nicht geladen werden.</v-alert>
  <v-card v-else>
    <v-data-table :items="users" :headers="headers" items-per-page="25" :sort-by="[{ key: 'name', order: 'asc' }]" no-data-text="Keine Benutzer gefunden" items-per-page-text="Einträge pro Seite">
      <template #item.name="{ item }">
        <div class="d-flex align-center ga-3 py-2"><AvatarImage type="users" :id="item.id" :has-avatar="item.hasAvatar" :initials="initialsOf(item.firstName, item.lastName)" :size="32" />{{ item.firstName }} {{ item.lastName }}</div>
      </template>
      <template #item.claims="{ item }"><v-chip v-for="claim in item.claims" :key="String(claim)" size="small" variant="tonal" class="mr-1">{{ roleLabel(claim) }}</v-chip></template>
      <template #item.passkeyCount="{ item }">
        <v-chip v-if="(item.passkeyCount ?? 0) > 0" size="small" color="success" variant="tonal" prepend-icon="mdi-key" :title="`${item.passkeyCount} Passkey(s) eingerichtet`">{{ item.passkeyCount }}</v-chip>
        <span v-else class="text-medium-emphasis" title="Kein Passkey eingerichtet">–</span>
      </template>
      <template #item.lastLoginAt="{ item }"><span class="text-no-wrap">{{ item.lastLoginAt ? formatDateTime(item.lastLoginAt) : 'Noch nie' }}</span></template>
      <template #item.actions="{ item }">
        <div class="d-flex justify-end">
          <span :title="canManage(item) ? 'Bearbeiten' : 'Systemadministratoren können nur von Systemadministratoren bearbeitet werden'"><v-btn icon="mdi-pencil" variant="text" size="small" :disabled="!canManage(item)" :aria-label="`${item.firstName} ${item.lastName} bearbeiten`" @click="open(item)" /></span>
          <v-btn v-if="canResetPasskeys(item)" icon="mdi-key-remove" variant="text" size="small" :aria-label="`Passkeys von ${item.firstName} ${item.lastName} zurücksetzen`" title="Passkeys zurücksetzen" @click="resettingPasskeys = item" />
          <span :title="canDelete(item) ? 'Löschen' : 'Systemadministratoren können nur von Systemadministratoren gelöscht werden'"><v-btn icon="mdi-delete" variant="text" size="small" color="error" :disabled="!canDelete(item)" :aria-label="`${item.firstName} ${item.lastName} löschen`" @click="deleting = item" /></span>
        </div>
      </template>
    </v-data-table>
  </v-card>

  <v-dialog v-model="dialog" max-width="600">
    <v-card :title="editing ? 'Benutzer bearbeiten' : 'Benutzer anlegen'">
      <v-form @submit.prevent="save">
        <v-card-text>
          <v-alert v-if="!editing" type="info" variant="tonal" density="compact" class="mb-4">Angelegte Benutzer erhalten keine E-Mail. Um jemanden mit Registrierungslink hinzuzufügen, nutze „Person einladen“.</v-alert>
          <v-text-field v-model="form.email" label="E-Mail *" type="email" :disabled="!!editing" />
          <v-row dense><v-col cols="6"><v-text-field v-model="form.firstName" label="Vorname *" /></v-col><v-col cols="6"><v-text-field v-model="form.lastName" label="Nachname *" /></v-col></v-row>
          <v-text-field v-model="form.phoneNumber" label="Telefonnummer" type="tel" />
          <template v-if="editing">
            <div class="text-title-small mt-2 mb-2">Rollen</div>
            <v-chip v-for="claim in claimOptions" :key="claim.value" class="mr-2 mb-2" :color="hasClaim(claim.value) ? 'primary' : undefined" :variant="hasClaim(claim.value) ? 'flat' : 'outlined'" :prepend-icon="hasClaim(claim.value) ? 'mdi-check' : undefined" :disabled="changingClaim !== null" @click="toggleClaim(claim.value)">{{ claim.title }}</v-chip>
          </template>
        </v-card-text>
        <v-card-actions><v-spacer /><v-btn :disabled="saving" @click="dialog = false">Abbrechen</v-btn><v-btn type="submit" color="primary" variant="flat" :loading="saving">Speichern</v-btn></v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>
  <InviteUserDialog v-model="inviteDialog" />
  <ConfirmDialog :model-value="!!deleting" title="Benutzer löschen?" text="Der Benutzer wird deaktiviert und kann sich nicht mehr anmelden." @update:model-value="!$event && (deleting = null)" @confirm="remove" />
  <ConfirmDialog :model-value="!!resettingPasskeys" title="Passkeys zurücksetzen?" :text="`Alle Passkeys von ${resettingPasskeys?.firstName ?? ''} ${resettingPasskeys?.lastName ?? ''} werden entfernt und alle Sitzungen beendet. Die Person meldet sich danach per Anmeldelink an und kann einen neuen Passkey einrichten.`" confirm-text="Zurücksetzen" @update:model-value="!$event && (resettingPasskeys = null)" @confirm="resetPasskeys" />
</template>
