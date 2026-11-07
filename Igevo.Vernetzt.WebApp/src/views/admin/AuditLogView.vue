<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import PageHeader from '@/components/PageHeader.vue'
import { api, toQuery } from '@/services/api'
import { auditAction, auditActions, entityName, formatJson, isEmptyGuid, note, userName } from '@/services/auditLog'
import { formatDateTime } from '@/services/dates'
import type { AuditAction, AuditLogDto, PagedResult } from '@/types/api'

const pageSize = 50
const page = ref(1); const entityType = ref<string | null>(null); const action = ref<AuditAction | null>(null); const includeRead = ref(false)
const selected = ref<AuditLogDto | null>(null)
const headers = [
  { title: 'Zeitpunkt', key: 'timestamp' },
  { title: 'Aktion', key: 'action' },
  { title: 'Entität', key: 'entity' },
  { title: 'Benutzer', key: 'user' },
  { title: 'IP-Adresse', key: 'ipAddress' },
  { title: 'Notiz', key: 'note' },
  { title: '', key: 'details', align: 'end' as const }
]
watch([entityType, action, includeRead], () => { page.value = 1 })
const query = useQuery({
  queryKey: computed(() => ['audit', page.value, entityType.value, action.value, includeRead.value]),
  queryFn: ({ signal }) => api.get<PagedResult<AuditLogDto>>(`/api/audit-logs${toQuery({ page: page.value, pageSize, entityType: entityType.value, action: action.value, includeReadAccess: includeRead.value })}`, signal),
  placeholderData: keepPreviousData
})
const entityTypes = useQuery({ queryKey: computed(() => ['audit', 'entity-types', includeRead.value]), queryFn: ({ signal }) => api.get<string[]>(`/api/audit-logs/entity-types${toQuery({ includeReadAccess: includeRead.value })}`, signal) })
const hasFilter = computed(() => !!entityType.value || action.value !== null || includeRead.value)
const range = computed(() => {
  const total = query.data.value?.totalCount ?? 0
  return total ? `${(page.value - 1) * pageSize + 1}–${Math.min(page.value * pageSize, total)} von ${total} Einträgen` : ''
})
function reset(): void { entityType.value = null; action.value = null; includeRead.value = false }
</script>

<template>
  <PageHeader title="Audit-Log" subtitle="Nachvollziehbarkeit aller Änderungen und Zugriffe" />
  <v-card class="mb-6">
    <v-card-text>
      <v-row dense align="center">
        <v-col cols="12" sm="6" md="4"><v-select v-model="entityType" label="Entitätstyp" :items="entityTypes.data.value ?? []" clearable hide-details /></v-col>
        <v-col cols="12" sm="6" md="4"><v-select v-model="action" label="Aktion" :items="auditActions" item-title="title" item-value="value" clearable hide-details /></v-col>
        <v-col cols="12" sm="6" md="2"><v-switch v-model="includeRead" label="Lesezugriffe" color="primary" hide-details density="compact" /></v-col>
        <v-col cols="12" sm="6" md="2" class="text-md-right"><v-btn variant="text" prepend-icon="mdi-filter-off" :disabled="!hasFilter" @click="reset">Zurücksetzen</v-btn></v-col>
      </v-row>
    </v-card-text>
  </v-card>

  <v-alert v-if="query.isError.value" type="error">Audit-Log konnte nicht geladen werden.</v-alert>
  <v-card v-else>
    <v-data-table :items="query.data.value?.items ?? []" :headers="headers" :loading="query.isFetching.value" :items-per-page="pageSize" density="comfortable" hide-default-footer no-data-text="Keine Einträge gefunden" loading-text="Lade Einträge …" hover @click:row="(_: unknown, row: { item: AuditLogDto }) => (selected = row.item)">
      <template #item.timestamp="{ item }"><span class="text-no-wrap" :title="new Date(item.timestamp).toLocaleString('de-DE')">{{ formatDateTime(item.timestamp) }}</span></template>
      <template #item.action="{ item }"><v-chip size="small" :color="auditAction(item.action).color" variant="flat">{{ auditAction(item.action).title }}</v-chip></template>
      <template #item.entity="{ item }">
        <div class="font-weight-medium">{{ entityName(item) }}</div>
        <div v-if="entityName(item) !== item.entityType" class="text-body-small text-medium-emphasis">{{ item.entityType }}</div>
      </template>
      <template #item.user="{ item }">{{ userName(item) }}</template>
      <template #item.ipAddress="{ item }"><span class="text-no-wrap">{{ item.ipAddress || '–' }}</span></template>
      <template #item.note="{ item }"><span class="audit-note">{{ note(item) || '–' }}</span></template>
      <template #item.details="{ item }"><v-btn icon="mdi-magnify-plus-outline" variant="text" size="small" aria-label="Details anzeigen" title="Details anzeigen" @click.stop="selected = item" /></template>
    </v-data-table>
    <div v-if="query.data.value?.totalCount" class="d-flex flex-wrap align-center justify-space-between ga-2 pa-3">
      <span class="text-body-medium text-medium-emphasis">{{ range }}</span>
      <v-pagination v-if="query.data.value.totalPages > 1" v-model="page" :length="query.data.value.totalPages" :total-visible="7" density="comfortable" />
    </div>
  </v-card>

  <v-dialog :model-value="!!selected" max-width="800" scrollable @update:model-value="!$event && (selected = null)">
    <v-card v-if="selected" title="Audit-Log-Eintrag" prepend-icon="mdi-history">
      <v-card-text>
        <v-row dense>
          <v-col cols="12" sm="6"><div class="text-body-small text-medium-emphasis">Zeitpunkt</div><div>{{ new Date(selected.timestamp).toLocaleString('de-DE') }}</div></v-col>
          <v-col cols="12" sm="6"><div class="text-body-small text-medium-emphasis">Aktion</div><v-chip size="small" :color="auditAction(selected.action).color" variant="flat">{{ auditAction(selected.action).title }}</v-chip></v-col>
          <v-col cols="12" sm="6"><div class="text-body-small text-medium-emphasis">Entitätstyp</div><div class="font-weight-medium">{{ selected.entityType }}</div></v-col>
          <v-col cols="12" sm="6"><div class="text-body-small text-medium-emphasis">Entität</div><div>{{ selected.entityDisplayName || '–' }}</div><code v-if="!isEmptyGuid(selected.entityId)" class="text-body-small text-medium-emphasis">{{ selected.entityId }}</code></v-col>
          <v-col cols="12" sm="6"><div class="text-body-small text-medium-emphasis">Benutzer</div><div>{{ userName(selected) }}</div><code v-if="!isEmptyGuid(selected.userId)" class="text-body-small text-medium-emphasis">{{ selected.userId }}</code></v-col>
          <v-col cols="12" sm="6"><div class="text-body-small text-medium-emphasis">IP-Adresse</div><div>{{ selected.ipAddress || '–' }}</div></v-col>
          <v-col v-if="selected.resolvedNotes ?? selected.notes" cols="12"><div class="text-body-small text-medium-emphasis">Notiz</div><div>{{ selected.resolvedNotes ?? selected.notes }}</div></v-col>
        </v-row>
        <template v-if="formatJson(selected.resolvedOldValues ?? selected.oldValues)">
          <div class="text-title-small text-error mt-4 mb-1"><v-icon icon="mdi-minus" size="small" /> Alter Wert</div>
          <pre class="audit-json audit-json--old">{{ formatJson(selected.resolvedOldValues ?? selected.oldValues) }}</pre>
        </template>
        <template v-if="formatJson(selected.resolvedNewValues ?? selected.newValues)">
          <div class="text-title-small text-success mt-4 mb-1"><v-icon icon="mdi-plus" size="small" /> Neuer Wert</div>
          <pre class="audit-json audit-json--new">{{ formatJson(selected.resolvedNewValues ?? selected.newValues) }}</pre>
        </template>
      </v-card-text>
      <v-card-actions><v-spacer /><v-btn @click="selected = null">Schließen</v-btn></v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.audit-note { display: inline-block; max-width: 240px; overflow-wrap: anywhere; }
:deep(.v-data-table__tr) { cursor: pointer; }
.audit-json { border-radius: 8px; font-size: .8rem; margin: 0; overflow-x: auto; padding: 12px; white-space: pre-wrap; word-break: break-word; }
.audit-json--old { background: rgba(var(--v-theme-error), .08); }
.audit-json--new { background: rgba(var(--v-theme-success), .08); }
</style>
