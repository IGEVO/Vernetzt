<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import { ApiError, api, toQuery } from '@/services/api'
import { formatDateTime } from '@/services/dates'
import { formatHolidayRange, yearRange } from '@/services/holidays'
import { useUiStore } from '@/stores/ui'
import type { HolidayEntryDto, HolidaySourceDto, SaveHolidayEntryDto, SaveHolidaySourceDto } from '@/types/api'

const ui = useUiStore()
const queryClient = useQueryClient()
const tab = ref<'entries' | 'sources'>('entries')
const year = ref(new Date().getFullYear())

const entries = useQuery({
  queryKey: computed(() => ['holidays', year.value]),
  queryFn: ({ signal }) => api.get<HolidayEntryDto[]>(`/api/holidays${toQuery(yearRange(year.value))}`, signal)
})

const sources = useQuery({
  queryKey: ['holiday-sources'],
  queryFn: ({ signal }) => api.get<HolidaySourceDto[]>('/api/holidays/sources', signal)
})

/** Calendar views and this page share the entry queries; every change reloads all of them. */
async function refresh(): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['holidays'] }),
    queryClient.invalidateQueries({ queryKey: ['holiday-sources'] })
  ])
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError && error.status === 400 ? error.message : fallback
}

// ─── Manual entries ────────────────────────────────────────────────────────

const entryDialog = ref(false)
const editingEntry = ref<HolidayEntryDto | null>(null)
const deletingEntry = ref<HolidayEntryDto | null>(null)
const entryForm = reactive<SaveHolidayEntryDto>({ title: '', startDate: '', endDate: '' })
const entryValid = computed(() => !!entryForm.title.trim() && !!entryForm.startDate && !!entryForm.endDate && entryForm.endDate >= entryForm.startDate)

function openEntry(item?: HolidayEntryDto): void {
  editingEntry.value = item ?? null
  Object.assign(entryForm, item
    ? { title: item.title, startDate: item.startDate, endDate: item.endDate }
    : { title: '', startDate: '', endDate: '' })
  entryDialog.value = true
}

function onStartDateChange(value: string): void {
  entryForm.startDate = value
  // Single-day entries are the common case: keep the end in step until it is set explicitly.
  if (!entryForm.endDate || entryForm.endDate < value) entryForm.endDate = value
}

async function saveEntry(): Promise<void> {
  if (!entryValid.value) return
  try {
    if (editingEntry.value) await api.put(`/api/holidays/${editingEntry.value.id}`, entryForm)
    else await api.post('/api/holidays', entryForm)
    entryDialog.value = false
    await refresh()
    ui.notify('Eintrag wurde gespeichert.', 'success')
  } catch (error) {
    ui.notify(errorMessage(error, 'Eintrag konnte nicht gespeichert werden.'), 'error')
  }
}

async function removeEntry(): Promise<void> {
  if (!deletingEntry.value) return
  try {
    await api.delete(`/api/holidays/${deletingEntry.value.id}`)
    deletingEntry.value = null
    await refresh()
    ui.notify('Eintrag wurde gelöscht.', 'success')
  } catch (error) {
    ui.notify(errorMessage(error, 'Eintrag konnte nicht gelöscht werden.'), 'error')
  }
}

// ─── Subscriptions ─────────────────────────────────────────────────────────

const sourceDialog = ref(false)
const savingSource = ref(false)
const syncingId = ref<string | null>(null)
const editingSource = ref<HolidaySourceDto | null>(null)
const deletingSource = ref<HolidaySourceDto | null>(null)
const sourceForm = reactive<SaveHolidaySourceDto>({ name: '', url: '', isEnabled: true })
const sourceValid = computed(() => !!sourceForm.name.trim() && /^(https|webcal):\/\/\S+$/i.test(sourceForm.url.trim()))

function openSource(item?: HolidaySourceDto): void {
  editingSource.value = item ?? null
  Object.assign(sourceForm, item
    ? { name: item.name, url: item.url, isEnabled: item.isEnabled }
    : { name: '', url: '', isEnabled: true })
  sourceDialog.value = true
}

function notifySyncResult(source: HolidaySourceDto): void {
  if (source.lastSyncError) ui.notify(`Abgleich fehlgeschlagen: ${source.lastSyncError}`, 'warning')
  else ui.notify(`Abonnement wurde gespeichert (${source.entryCount} Einträge).`, 'success')
}

async function saveSource(): Promise<void> {
  if (!sourceValid.value) return
  savingSource.value = true
  try {
    const saved = editingSource.value
      ? await api.put<HolidaySourceDto>(`/api/holidays/sources/${editingSource.value.id}`, sourceForm)
      : await api.post<HolidaySourceDto>('/api/holidays/sources', sourceForm)
    sourceDialog.value = false
    await refresh()
    notifySyncResult(saved)
  } catch (error) {
    ui.notify(errorMessage(error, 'Abonnement konnte nicht gespeichert werden.'), 'error')
  } finally {
    savingSource.value = false
  }
}

async function syncSource(source: HolidaySourceDto): Promise<void> {
  syncingId.value = source.id
  try {
    const result = await api.post<HolidaySourceDto>(`/api/holidays/sources/${source.id}/sync`)
    await refresh()
    if (result.lastSyncError) ui.notify(`Abgleich fehlgeschlagen: ${result.lastSyncError}`, 'warning')
    else ui.notify(`Abgleich erfolgreich (${result.entryCount} Einträge).`, 'success')
  } catch {
    ui.notify('Abgleich konnte nicht gestartet werden.', 'error')
  } finally {
    syncingId.value = null
  }
}

async function removeSource(): Promise<void> {
  if (!deletingSource.value) return
  try {
    await api.delete(`/api/holidays/sources/${deletingSource.value.id}`)
    deletingSource.value = null
    await refresh()
    ui.notify('Abonnement wurde gelöscht.', 'success')
  } catch {
    ui.notify('Abonnement konnte nicht gelöscht werden.', 'error')
  }
}
</script>

<template>
  <PageHeader
    title="Ferien & Feiertage"
    subtitle="Werden in allen Kalenderansichten im Hintergrund angezeigt, aber nie in iCal-Abos exportiert."
  >
    <template #actions>
      <v-btn v-if="tab === 'entries'" color="primary" prepend-icon="mdi-plus" @click="openEntry()">Eintrag hinzufügen</v-btn>
      <v-btn v-else color="primary" prepend-icon="mdi-plus" @click="openSource()">Abonnement hinzufügen</v-btn>
    </template>
  </PageHeader>

  <v-tabs v-model="tab" color="primary" class="mb-4">
    <v-tab value="entries" prepend-icon="mdi-calendar-star">Einträge</v-tab>
    <v-tab value="sources" prepend-icon="mdi-calendar-sync">Abonnements</v-tab>
  </v-tabs>

  <!-- ═══ ENTRIES ═══════════════════════════════════════════════════════════ -->
  <template v-if="tab === 'entries'">
    <div class="d-flex align-center ga-2 mb-3">
      <v-btn icon="mdi-chevron-left" variant="text" density="comfortable" aria-label="Vorheriges Jahr" @click="year--" />
      <span class="text-title-medium font-weight-medium">{{ year }}</span>
      <v-btn icon="mdi-chevron-right" variant="text" density="comfortable" aria-label="Nächstes Jahr" @click="year++" />
    </div>

    <v-progress-linear v-if="entries.isPending.value" indeterminate />
    <v-alert v-else-if="entries.isError.value" type="error">Einträge konnten nicht geladen werden.</v-alert>
    <v-empty-state
      v-else-if="!entries.data.value?.length"
      icon="mdi-calendar-blank"
      title="Keine Einträge"
      :text="`Für ${year} gibt es weder manuelle Einträge noch Einträge aus Abonnements.`"
    />
    <v-table v-else>
      <thead>
        <tr><th>Name</th><th>Zeitraum</th><th>Quelle</th><th class="text-right">Aktionen</th></tr>
      </thead>
      <tbody>
        <tr v-for="item in entries.data.value" :key="item.id">
          <td>{{ item.title }}</td>
          <td class="text-no-wrap">{{ formatHolidayRange(item) }}</td>
          <td>
            <v-chip v-if="item.sourceId" size="small" variant="tonal" prepend-icon="mdi-calendar-sync">{{ item.sourceName }}</v-chip>
            <v-chip v-else size="small" variant="tonal" color="primary">Manuell</v-chip>
          </td>
          <td class="text-right text-no-wrap">
            <template v-if="!item.sourceId">
              <v-btn icon="mdi-pencil" variant="text" size="small" :aria-label="`${item.title} bearbeiten`" title="Bearbeiten" @click="openEntry(item)" />
              <v-btn icon="mdi-delete" variant="text" size="small" color="error" :aria-label="`${item.title} löschen`" title="Löschen" @click="deletingEntry = item" />
            </template>
          </td>
        </tr>
      </tbody>
    </v-table>
  </template>

  <!-- ═══ SUBSCRIPTIONS ═════════════════════════════════════════════════════ -->
  <template v-else>
    <p class="text-body-medium text-medium-emphasis mb-4">
      iCal-Adressen mit Schulferien oder Feiertagen (z. B. des Bundeslands) werden täglich abgeglichen.
      Importierte Einträge sind schreibgeschützt; schlägt ein Abgleich fehl, bleiben die bisherigen Einträge erhalten.
    </p>
    <v-progress-linear v-if="sources.isPending.value" indeterminate />
    <v-alert v-else-if="sources.isError.value" type="error">Abonnements konnten nicht geladen werden.</v-alert>
    <v-empty-state v-else-if="!sources.data.value?.length" icon="mdi-calendar-sync" title="Keine Abonnements" />
    <v-table v-else>
      <thead>
        <tr><th>Name</th><th>Status</th><th>Letzter Abgleich</th><th class="text-right">Einträge</th><th class="text-right">Aktionen</th></tr>
      </thead>
      <tbody>
        <tr v-for="item in sources.data.value" :key="item.id">
          <td>
            <div>{{ item.name }}</div>
            <div class="text-body-small text-medium-emphasis text-truncate source-url" :title="item.url">{{ item.url }}</div>
          </td>
          <td>
            <v-chip v-if="!item.isEnabled" size="small">Inaktiv</v-chip>
            <v-chip v-else-if="item.lastSyncError" size="small" color="warning" :title="item.lastSyncError">Fehler</v-chip>
            <v-chip v-else size="small" color="success">Aktiv</v-chip>
          </td>
          <td>
            <div class="text-no-wrap">{{ formatDateTime(item.lastSyncedAt) }}</div>
            <div v-if="item.lastSyncError" class="text-body-small text-warning">{{ item.lastSyncError }}</div>
          </td>
          <td class="text-right">{{ item.entryCount }}</td>
          <td class="text-right text-no-wrap">
            <v-btn
              icon="mdi-sync"
              variant="text"
              size="small"
              :disabled="!item.isEnabled"
              :loading="syncingId === item.id"
              :aria-label="`${item.name} jetzt abgleichen`"
              title="Jetzt abgleichen"
              @click="syncSource(item)"
            />
            <v-btn icon="mdi-pencil" variant="text" size="small" :aria-label="`${item.name} bearbeiten`" title="Bearbeiten" @click="openSource(item)" />
            <v-btn icon="mdi-delete" variant="text" size="small" color="error" :aria-label="`${item.name} löschen`" title="Löschen" @click="deletingSource = item" />
          </td>
        </tr>
      </tbody>
    </v-table>
  </template>

  <!-- ═══ DIALOGS ═══════════════════════════════════════════════════════════ -->
  <v-dialog v-model="entryDialog" max-width="500">
    <v-card :title="editingEntry ? 'Eintrag bearbeiten' : 'Eintrag hinzufügen'">
      <v-form @submit.prevent="saveEntry">
        <v-card-text>
          <v-text-field v-model="entryForm.title" label="Name *" placeholder="z. B. Kirmes oder Brückentag" maxlength="200" />
          <div class="d-flex ga-3">
            <v-text-field :model-value="entryForm.startDate" type="date" label="Von *" @update:model-value="onStartDateChange" />
            <v-text-field v-model="entryForm.endDate" type="date" label="Bis *" :min="entryForm.startDate" />
          </div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="entryDialog = false">Abbrechen</v-btn>
          <v-btn type="submit" color="primary" variant="flat" :disabled="!entryValid">Speichern</v-btn>
        </v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>

  <v-dialog v-model="sourceDialog" max-width="600">
    <v-card :title="editingSource ? 'Abonnement bearbeiten' : 'Abonnement hinzufügen'">
      <v-form @submit.prevent="saveSource">
        <v-card-text>
          <v-text-field v-model="sourceForm.name" label="Name *" placeholder="z. B. Schulferien NRW" maxlength="200" />
          <v-text-field
            v-model="sourceForm.url"
            label="iCal-Adresse *"
            placeholder="https://… oder webcal://…"
            hint="Nur HTTPS- oder webcal-Adressen. Der Kalender wird nach dem Speichern sofort abgeglichen."
            persistent-hint
          />
          <v-switch v-model="sourceForm.isEnabled" label="Aktiv" color="primary" class="mt-2" />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="sourceDialog = false">Abbrechen</v-btn>
          <v-btn type="submit" color="primary" variant="flat" :disabled="!sourceValid" :loading="savingSource">Speichern</v-btn>
        </v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>

  <ConfirmDialog
    :model-value="!!deletingEntry"
    title="Eintrag löschen?"
    text="Der Eintrag wird aus allen Kalenderansichten entfernt."
    @update:model-value="!$event && (deletingEntry = null)"
    @confirm="removeEntry"
  />
  <ConfirmDialog
    :model-value="!!deletingSource"
    title="Abonnement löschen?"
    text="Alle daraus importierten Einträge werden ebenfalls entfernt."
    @update:model-value="!$event && (deletingSource = null)"
    @confirm="removeSource"
  />
</template>

<style scoped>
.source-url {
  max-width: 360px;
}
</style>
