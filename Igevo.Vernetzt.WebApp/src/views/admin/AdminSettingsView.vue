<script setup lang="ts">
import { reactive, ref, watchEffect } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import CalendarFeedLink from '@/components/CalendarFeedLink.vue'
import PageHeader from '@/components/PageHeader.vue'
import PortalSettingsCard from '@/components/admin/PortalSettingsCard.vue'
import { api } from '@/services/api'
import { backupTriggerLabel, currentBackupError, formatFileSize } from '@/services/backups'
import { formatDateTime } from '@/services/dates'
import { useUiStore } from '@/stores/ui'
import type { AppSettingsDto, BackupFileDto, BackupStatusDto, CalendarFeedTokenDto } from '@/types/api'

const ui = useUiStore(); const saving = ref(false)
const model = reactive({ contactName: '', contactEmail: '', privacyPolicyUrl: '' })
const settings = useQuery({ queryKey: ['admin', 'settings'], queryFn: ({ signal }) => api.get<AppSettingsDto>('/api/settings', signal) })
const backups = useQuery({ queryKey: ['admin', 'backups'], queryFn: ({ signal }) => api.get<BackupStatusDto>('/api/backups', signal) })
const backingUp = ref(false)
const feeds = useQuery({ queryKey: ['calendar-feeds', 'global'], queryFn: ({ signal }) => api.get<CalendarFeedTokenDto[]>('/api/calendar-feeds/tokens?globalOnly=true', signal) })
watchEffect(() => { if (settings.data.value) Object.assign(model, settings.data.value) })
const feed = (highlights: boolean) => (feeds.data.value ?? []).find((item) => !item.isRevoked && item.highlightsOnly === highlights)
const globalFeeds = [
  { title: 'Alle Termine', subtitle: 'Alle Termine aller Vereine und Abteilungen', highlights: false },
  { title: 'Highlights', subtitle: 'Nur als Highlight markierte Termine', highlights: true }
]
const missing = () => !model.contactName?.trim() || !model.contactEmail?.trim() || !model.privacyPolicyUrl?.trim()

async function backupNow(): Promise<void> {
  backingUp.value = true
  try { const backup = await api.post<BackupFileDto>('/api/backups'); ui.notify(`Sicherung ${backup.fileName} wurde erstellt.`, 'success') }
  catch { ui.notify('Die Sicherung ist fehlgeschlagen.', 'error') }
  finally { backingUp.value = false; await backups.refetch() }
}

async function save(): Promise<void> {
  saving.value = true
  try { await api.put('/api/settings', model); await settings.refetch(); ui.notify('Einstellungen wurden gespeichert.', 'success') }
  catch { ui.notify('Einstellungen konnten nicht gespeichert werden.', 'error') }
  finally { saving.value = false }
}
</script>

<template>
  <PageHeader title="Systemeinstellungen" />
  <v-progress-linear v-if="settings.isPending.value" indeterminate color="primary" />
  <v-alert v-else-if="settings.isError.value" type="error">Einstellungen konnten nicht geladen werden.</v-alert>
  <v-row v-else>
    <v-col cols="12" md="6">
      <v-card height="100%">
        <v-card-item title="Ansprechpartner & Datenschutz" subtitle="Erscheint im Fuß jeder E-Mail" prepend-icon="mdi-card-account-mail" />
        <v-form @submit.prevent="save">
          <v-card-text>
            <v-alert v-if="missing()" type="warning" variant="tonal" class="mb-4">Solange nicht alle drei Felder ausgefüllt sind, können keine Einladungen versendet werden.</v-alert>
            <v-text-field v-model="model.contactName" label="Name *" />
            <v-text-field v-model="model.contactEmail" label="E-Mail-Adresse *" type="email" />
            <v-text-field v-model="model.privacyPolicyUrl" label="URL der Datenschutzerklärung *" placeholder="https://" />
          </v-card-text>
          <v-card-actions><v-spacer /><v-btn type="submit" color="primary" variant="flat" :loading="saving">Speichern</v-btn></v-card-actions>
        </v-form>
      </v-card>
    </v-col>
    <v-col cols="12" md="6">
      <v-card height="100%">
        <v-card-item title="Kalender-Links (global)" subtitle="iCal-Abonnements für alle Termine" prepend-icon="mdi-calendar-sync" />
        <v-card-text>
          <v-progress-linear v-if="feeds.isPending.value" indeterminate />
          <template v-for="(item, index) in globalFeeds" v-else :key="item.title">
            <v-divider v-if="index > 0" class="my-4" />
            <CalendarFeedLink :title="item.title" :subtitle="item.subtitle" :token="feed(item.highlights)" :create-body="{ scope: 2, includeDepartments: true, highlightsOnly: item.highlights }" @changed="feeds.refetch()" />
          </template>
        </v-card-text>
      </v-card>
    </v-col>
    <v-col cols="12">
      <PortalSettingsCard />
    </v-col>
    <v-col cols="12">
      <v-card>
        <v-card-item title="Datensicherung" subtitle="Sicherungen der Datenbank (ohne hochgeladene Dateien)" prepend-icon="mdi-database-arrow-down" />
        <v-card-text>
          <v-progress-linear v-if="backups.isPending.value" indeterminate />
          <v-alert v-else-if="backups.isError.value" type="error" variant="tonal">Status der Datensicherung konnte nicht geladen werden.</v-alert>
          <template v-else-if="backups.data.value">
            <v-alert v-if="currentBackupError(backups.data.value)" type="error" variant="tonal" class="mb-4" title="Letzte Sicherung fehlgeschlagen">
              {{ formatDateTime(backups.data.value.lastFailureUtc) }}: {{ currentBackupError(backups.data.value) }}
            </v-alert>
            <v-row density="compact" class="mb-2">
              <v-col cols="12" sm="6" md="3">
                <div class="text-label-large text-medium-emphasis">Automatische Sicherung</div>
                <div v-if="backups.data.value.scheduleEnabled">täglich um {{ backups.data.value.scheduleTime }} Uhr<br><span class="text-medium-emphasis">nächste: {{ formatDateTime(backups.data.value.nextRunUtc) }}</span></div>
                <div v-else>deaktiviert</div>
              </v-col>
              <v-col cols="12" sm="6" md="3">
                <div class="text-label-large text-medium-emphasis">Aufbewahrung</div>
                <div>{{ backups.data.value.keepDaily }} Tage täglich, {{ backups.data.value.keepWeekly }} Wochen wöchentlich</div>
              </v-col>
              <v-col cols="12" sm="6" md="3">
                <div class="text-label-large text-medium-emphasis">Vor Updates</div>
                <div>{{ backups.data.value.beforeMigration ? 'Sicherung vor Datenbank-Änderungen' : 'keine Sicherung' }}</div>
              </v-col>
              <v-col cols="12" sm="6" md="3">
                <div class="text-label-large text-medium-emphasis">Ablage</div>
                <code class="text-break">{{ backups.data.value.directory }}</code>
              </v-col>
            </v-row>
            <p v-if="backups.data.value.backups.length === 0" class="text-body-medium text-medium-emphasis mb-0">Noch keine Sicherung vorhanden.</p>
            <v-table v-else density="compact" fixed-header :height="backups.data.value.backups.length > 8 ? 320 : undefined">
              <thead><tr><th>Zeitpunkt</th><th>Anlass</th><th class="text-end">Größe</th><th class="d-none d-md-table-cell">Datei</th></tr></thead>
              <tbody>
                <tr v-for="item in backups.data.value.backups" :key="item.fileName">
                  <td>{{ formatDateTime(item.createdUtc) }}</td>
                  <td>{{ backupTriggerLabel(item.trigger) }}</td>
                  <td class="text-end">{{ formatFileSize(item.sizeBytes) }}</td>
                  <td class="d-none d-md-table-cell"><code>{{ item.fileName }}</code></td>
                </tr>
              </tbody>
            </v-table>
          </template>
        </v-card-text>
        <v-card-actions><v-spacer /><v-btn color="primary" variant="flat" prepend-icon="mdi-content-save" :loading="backingUp" :disabled="!backups.data.value" @click="backupNow">Jetzt sichern</v-btn></v-card-actions>
      </v-card>
    </v-col>
  </v-row>
</template>
