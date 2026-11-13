<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import { ApiError, api } from '@/services/api'
import { formatDate, formatDateTime } from '@/services/dates'
import { organizationPath } from '@/services/organizationPath'
import { useUiStore } from '@/stores/ui'
import type { PendingInvitationDto, PendingItemsOverviewDto } from '@/types/api'

type Tab = 'invitations' | 'assignments' | 'contextEmails'
interface OrganizationItem { clubId?: string | null; departmentId?: string | null; clubName?: string | null; parentDepartmentName?: string | null; departmentName?: string | null; positionName?: string | null }

/** Consent reminders are limited to one per 24 hours by the server. */
const REMINDER_COOLDOWN_MS = 24 * 60 * 60 * 1000
const QUERY_KEY = ['pending-items']

const ui = useUiStore()
const queryClient = useQueryClient()
const tab = ref<Tab>('invitations')
const search = ref('')
const busyId = ref<string | null>(null)
const revoking = ref<PendingInvitationDto | null>(null)

const query = useQuery({
  queryKey: QUERY_KEY,
  queryFn: ({ signal }) => api.get<PendingItemsOverviewDto>('/api/pending-items', signal)
})

const counts = computed(() => ({
  invitations: query.data.value?.invitations.length ?? 0,
  assignments: query.data.value?.assignments.length ?? 0,
  contextEmails: query.data.value?.contextEmails.length ?? 0
}))

const tabs: Array<{ value: Tab; title: string; icon: string }> = [
  { value: 'invitations', title: 'Einladungen', icon: 'mdi-email-fast' },
  { value: 'assignments', title: 'Zuordnungen', icon: 'mdi-account-clock' },
  { value: 'contextEmails', title: 'Amtsadressen', icon: 'mdi-email-check' }
]

const orgLabel = (item: OrganizationItem) => organizationPath(item, '–')
const orgLink = (item: OrganizationItem) => item.departmentId ? `/departments/${item.departmentId}` : item.clubId ? `/clubs/${item.clubId}` : undefined
const roleLabel = (claim: string | number) => ({ 0: 'Systemadmin', 1: 'Manager', 2: 'Leserechte', SystemAdmin: 'Systemadmin', Manager: 'Manager', DataReader: 'Leserechte' } as Record<string, string>)[String(claim)] ?? String(claim)
const reminderBlocked = (lastSent?: string | null) => !!lastSent && Date.now() - new Date(lastSent).getTime() < REMINDER_COOLDOWN_MS

const invitationHeaders = [
  { title: 'E-Mail', key: 'email' },
  { title: 'Zuordnung', key: 'organization', sortable: false },
  { title: 'Eingeladen von', key: 'invitedByName' },
  { title: 'Eingeladen am', key: 'createdAt' },
  { title: 'Gültig bis', key: 'expiresAt' },
  { title: '', key: 'actions', sortable: false, align: 'end' as const }
]
const assignmentHeaders = [
  { title: 'Person', key: 'userName' },
  { title: 'Zuordnung', key: 'organization', sortable: false },
  { title: 'Zugeordnet von', key: 'assignedByName' },
  { title: 'Zugeordnet am', key: 'assignedAt' },
  { title: 'Zuletzt angefragt', key: 'lastConsentRequestAt' },
  { title: '', key: 'actions', sortable: false, align: 'end' as const }
]
const contextEmailHeaders = [
  { title: 'Amtsadresse', key: 'contextEmail' },
  { title: 'Person', key: 'userName' },
  { title: 'Zuordnung', key: 'organization', sortable: false },
  { title: 'Zuletzt versendet', key: 'lastVerificationSentAt' },
  { title: '', key: 'actions', sortable: false, align: 'end' as const }
]

/** v-data-table only searches plain columns; the organization path is added so it can be found too. */
function withOrganization<T extends OrganizationItem>(items: T[] | undefined): Array<T & { organization: string }> {
  return (items ?? []).map((item) => ({ ...item, organization: orgLabel(item) }))
}
const invitations = computed(() => withOrganization(query.data.value?.invitations))
const assignments = computed(() => withOrganization(query.data.value?.assignments))
const contextEmails = computed(() => withOrganization(query.data.value?.contextEmails))

async function refresh(): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: QUERY_KEY })
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError && error.status !== 500 ? error.message : fallback
}

async function run(id: string, action: () => Promise<unknown>, success: string, failure: string): Promise<void> {
  busyId.value = id
  try {
    await action()
    ui.notify(success, 'success')
  } catch (error) {
    ui.notify(errorMessage(error, failure), 'error')
  } finally {
    busyId.value = null
    await refresh()
  }
}

async function revoke(): Promise<void> {
  const invitation = revoking.value
  if (!invitation) return
  revoking.value = null
  await run(invitation.id, () => api.delete(`/api/pending-items/invitations/${invitation.id}`),
    `Einladung an ${invitation.email} wurde widerrufen.`, 'Einladung konnte nicht widerrufen werden.')
}

function remindAssignment(item: { assignmentId: string; userName: string }): Promise<void> {
  return run(item.assignmentId, () => api.post(`/api/users/contact-assignments/${item.assignmentId}/consent-reminder`),
    `Erinnerung an ${item.userName} wurde versendet.`, 'Erinnerung konnte nicht versendet werden.')
}

function resendContextEmail(item: { assignmentId: string; userId: string; contextEmail: string }): Promise<void> {
  return run(item.assignmentId, () => api.post(`/api/users/${item.userId}/contact-assignments/${item.assignmentId}/email/resend-verification`),
    `Bestätigungs-E-Mail an ${item.contextEmail} wurde erneut versendet.`, 'Bestätigungs-E-Mail konnte nicht versendet werden.')
}
</script>

<template>
  <PageHeader title="Ausstehende Bestätigungen" subtitle="Einladungen, Zuordnungen und Amtsadressen, bei denen noch eine Reaktion der Person fehlt." />

  <v-tabs v-model="tab" color="primary" class="mb-4" show-arrows>
    <v-tab v-for="item in tabs" :key="item.value" :value="item.value" :prepend-icon="item.icon">
      {{ item.title }}
      <v-badge v-if="counts[item.value]" :content="counts[item.value]" color="primary" inline />
    </v-tab>
  </v-tabs>

  <v-text-field v-model="search" label="Suchen" placeholder="Name, E-Mail oder Organisation" prepend-inner-icon="mdi-magnify" clearable hide-details class="mb-4" />

  <v-progress-linear v-if="query.isPending.value" indeterminate color="primary" />
  <v-alert v-else-if="query.isError.value" type="error">Ausstehende Bestätigungen konnten nicht geladen werden.</v-alert>

  <v-card v-else-if="tab === 'invitations'">
    <v-data-table :items="invitations" :headers="invitationHeaders" :search="search" item-value="id" items-per-page="25" no-data-text="Keine offenen Einladungen" items-per-page-text="Einträge pro Seite">
      <template #item.email="{ item }">
        <div>{{ item.email }}</div>
        <div v-if="item.contextEmail" class="text-body-small text-medium-emphasis">Amtsadresse: {{ item.contextEmail }}</div>
        <v-chip v-for="claim in item.pendingClaims" :key="String(claim)" size="x-small" variant="tonal" class="mr-1 mt-1">{{ roleLabel(claim) }}</v-chip>
      </template>
      <template #item.organization="{ item }">
        <router-link v-if="orgLink(item)" :to="orgLink(item)!">{{ item.organization }}</router-link>
        <span v-else class="text-medium-emphasis">Ohne Zuordnung</span>
        <div v-if="item.positionName" class="text-body-small text-medium-emphasis">{{ item.positionName }}</div>
      </template>
      <template #item.invitedByName="{ item }">{{ item.invitedByName ?? '–' }}</template>
      <template #item.createdAt="{ item }"><span class="text-no-wrap">{{ formatDate(item.createdAt) }}</span></template>
      <template #item.expiresAt="{ item }"><span class="text-no-wrap">{{ formatDate(item.expiresAt) }}</span></template>
      <template #item.actions="{ item }">
        <v-btn icon="mdi-email-remove" variant="text" size="small" color="error" :loading="busyId === item.id" :aria-label="`Einladung an ${item.email} widerrufen`" title="Widerrufen" @click="revoking = item" />
      </template>
    </v-data-table>
  </v-card>

  <v-card v-else-if="tab === 'assignments'">
    <v-data-table :items="assignments" :headers="assignmentHeaders" :search="search" item-value="assignmentId" items-per-page="25" no-data-text="Keine ausstehenden Zuordnungen" items-per-page-text="Einträge pro Seite">
      <template #item.userName="{ item }">
        <div>{{ item.userName }}</div>
        <div class="text-body-small text-medium-emphasis">{{ item.userEmail }}</div>
      </template>
      <template #item.organization="{ item }">
        <router-link v-if="orgLink(item)" :to="orgLink(item)!">{{ item.organization }}</router-link>
        <div v-if="item.positionName" class="text-body-small text-medium-emphasis">{{ item.positionName }}</div>
      </template>
      <template #item.assignedByName="{ item }">{{ item.assignedByName ?? '–' }}</template>
      <template #item.assignedAt="{ item }"><span class="text-no-wrap">{{ formatDate(item.assignedAt) }}</span></template>
      <template #item.lastConsentRequestAt="{ item }"><span class="text-no-wrap">{{ formatDateTime(item.lastConsentRequestAt) }}</span></template>
      <template #item.actions="{ item }">
        <span :title="reminderBlocked(item.lastConsentRequestAt) ? 'In den letzten 24 Stunden bereits erinnert' : 'Erinnern'">
          <v-btn icon="mdi-bell-ring" variant="text" size="small" :disabled="reminderBlocked(item.lastConsentRequestAt)" :loading="busyId === item.assignmentId" :aria-label="`${item.userName} an Zustimmung erinnern`" @click="remindAssignment(item)" />
        </span>
      </template>
    </v-data-table>
  </v-card>

  <v-card v-else>
    <v-data-table :items="contextEmails" :headers="contextEmailHeaders" :search="search" item-value="assignmentId" items-per-page="25" no-data-text="Keine unbestätigten Amtsadressen" items-per-page-text="Einträge pro Seite">
      <template #item.userName="{ item }">
        <div>{{ item.userName }}</div>
        <v-chip v-if="!item.isAssignmentConfirmed" size="x-small" color="warning" variant="tonal" class="mt-1">Zuordnung unbestätigt</v-chip>
      </template>
      <template #item.organization="{ item }">
        <router-link v-if="orgLink(item)" :to="orgLink(item)!">{{ item.organization }}</router-link>
        <div v-if="item.positionName" class="text-body-small text-medium-emphasis">{{ item.positionName }}</div>
      </template>
      <template #item.lastVerificationSentAt="{ item }"><span class="text-no-wrap">{{ formatDateTime(item.lastVerificationSentAt) }}</span></template>
      <template #item.actions="{ item }">
        <v-btn icon="mdi-email-sync" variant="text" size="small" :loading="busyId === item.assignmentId" :aria-label="`Bestätigung an ${item.contextEmail} erneut senden`" title="Bestätigung erneut senden" @click="resendContextEmail(item)" />
      </template>
    </v-data-table>
  </v-card>

  <ConfirmDialog
    :model-value="!!revoking"
    title="Einladung widerrufen?"
    :text="`Der Einladungslink an ${revoking?.email ?? ''} wird sofort ungültig. Du kannst die Person später erneut einladen.`"
    confirm-text="Widerrufen"
    @update:model-value="!$event && (revoking = null)"
    @confirm="revoke"
  />
</template>
