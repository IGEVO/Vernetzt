<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import AvatarImage from '@/components/AvatarImage.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import RichTextEditor from '@/components/RichTextEditor.vue'
import { api } from '@/services/api'
import { initialsOf } from '@/services/avatars'
import { formatDateTime } from '@/services/dates'
import { htmlToText, sanitizeEmailHtml } from '@/services/html'
import { useUiStore } from '@/stores/ui'
import type { LocationSummaryDto, NamedEntityDto, RecipientCandidateDto } from '@/types/api'
import { organizationPath } from '@/services/organizationPath'

interface RelayResult { tokenId: string; tokenAddress: string; expiresAt: string; recipientCount: number; maxMessageSizeBytes: number }
interface SendResult { success: boolean; recipientCount: number; errorMessage?: string | null }

const router = useRouter(); const ui = useUiStore()
const steps = ['Schnellauswahl', 'Empfänger bestätigen', 'Versandart', 'Verfassen & Senden']
const step = ref(1)

// ── Data ─────────────────────────────────────────────────────────────────────
const candidates = useQuery({ queryKey: ['recipient-candidates'], queryFn: ({ signal }) => api.get<RecipientCandidateDto[]>('/api/users/recipient-candidates', signal) })
const clubTypes = useQuery({ queryKey: ['club-types', 'active'], queryFn: ({ signal }) => api.get<NamedEntityDto[]>('/api/clubtypes/active', signal) })
const positions = useQuery({ queryKey: ['contact-positions', 'active'], queryFn: ({ signal }) => api.get<NamedEntityDto[]>('/api/contactpositions/active', signal) })
const locations = useQuery({ queryKey: ['locations', 'active'], queryFn: ({ signal }) => api.get<LocationSummaryDto[]>('/api/locations/active', signal) })
const relayEnabled = useQuery({ queryKey: ['relay-status'], queryFn: ({ signal }) => api.get<boolean>('/api/email/relay/status', signal) })
const all = computed(() => candidates.data.value ?? [])
const keyOf = (item: RecipientCandidateDto) => item.assignmentId || item.userId
const clubIdOf = (item: RecipientCandidateDto) => item.parentClubId ?? item.organizationId
const clubNameOf = (item: RecipientCandidateDto) => item.parentClubName ?? item.organizationName
const orgLabel = (item: RecipientCandidateDto) =>
  organizationPath({ clubName: item.parentClubName, parentDepartmentName: item.parentDepartmentName, departmentName: item.organizationName }, item.organizationName)

// ── Step 1: quick selection by filters (AND between groups, OR within a group) ──
const filterClubTypes = ref<string[]>([]); const filterPositions = ref<string[]>([]); const filterClubs = ref<string[]>([]); const filterLocations = ref<string[]>([])
const clubOptions = computed(() => {
  const clubs = new Map<string, string>()
  for (const item of all.value) clubs.set(clubIdOf(item), clubNameOf(item))
  return [...clubs].map(([value, title]) => ({ value, title, count: all.value.filter((item) => clubIdOf(item) === value).length })).sort((a, b) => a.title.localeCompare(b.title, 'de'))
})
const countBy = (predicate: (item: RecipientCandidateDto) => boolean) => all.value.filter(predicate).length
const hasFilters = computed(() => !!(filterClubTypes.value.length || filterPositions.value.length || filterClubs.value.length || filterLocations.value.length))
/** Number of organizations (club, department or group) that use the location themselves. */
const organizationCountFor = (locationId: string) => new Set(all.value.filter((item) => item.isAssignmentRecipient && item.locationIds?.includes(locationId)).map((item) => item.organizationId)).size
/** Organizations in which at least one visible contact person is flagged to receive location information. */
const flaggedOrganizations = computed(() => new Set(all.value.filter((item) => item.isAssignmentRecipient && item.receivesLocationInfo).map((item) => item.organizationId)))
/**
 * Location filter: the organization uses one of the selected locations (own locations only, no inheritance).
 * Per organization only flagged contact persons are chosen; if nobody is flagged, all of them.
 * An active position filter replaces the flag logic, so the combination cannot end up empty.
 */
const matchesLocation = (item: RecipientCandidateDto) => !filterLocations.value.length || (
  item.isAssignmentRecipient && !!item.locationIds?.some((id) => filterLocations.value.includes(id)) &&
  (filterPositions.value.length > 0 || !!item.receivesLocationInfo || !flaggedOrganizations.value.has(item.organizationId)))
const filterMatches = computed(() => hasFilters.value ? all.value.filter((item) =>
  (!filterClubTypes.value.length || (!!item.clubTypeId && filterClubTypes.value.includes(item.clubTypeId))) &&
  (!filterPositions.value.length || (!!item.positionId && filterPositions.value.includes(item.positionId))) &&
  (!filterClubs.value.length || filterClubs.value.includes(clubIdOf(item))) &&
  matchesLocation(item)) : [])
function resetFilters(): void { filterClubTypes.value = []; filterPositions.value = []; filterClubs.value = []; filterLocations.value = [] }

// ── Step 2: confirm individual recipients ───────────────────────────────────
const selected = ref<Set<string>>(new Set())
const preselection = ref<Set<string>>(new Set())
const search = ref(''); const showAll = ref(false)
/** Which location rule was applied to the preselection (for the hint in step 2). */
const appliedLocationRule = ref<'none' | 'flags' | 'positions'>('none')
function toStep2(): void {
  appliedLocationRule.value = hasFilters.value && filterLocations.value.length ? (filterPositions.value.length ? 'positions' : 'flags') : 'none'
  // Applying the quick selection replaces the previous selection, but only if filters are active.
  if (hasFilters.value) { selected.value = new Set(filterMatches.value.map(keyOf)); preselection.value = new Set(selected.value) }
  else preselection.value = new Set()
  showAll.value = !hasFilters.value
  step.value = 2
}
const listed = computed(() => {
  const term = (search.value ?? '').trim().toLocaleLowerCase('de')
  if (term) return all.value.filter((item) => `${item.firstName} ${item.lastName} ${item.displayEmail ?? ''} ${orgLabel(item)} ${item.positionName ?? ''} ${item.assignmentDetails ?? ''}`.toLocaleLowerCase('de').includes(term))
  if (showAll.value) return all.value
  return all.value.filter((item) => preselection.value.has(keyOf(item)) || selected.value.has(keyOf(item)))
})
const isSelected = (item: RecipientCandidateDto) => selected.value.has(keyOf(item))
function toggle(item: RecipientCandidateDto): void { const next = new Set(selected.value); if (next.has(keyOf(item))) next.delete(keyOf(item)); else next.add(keyOf(item)); selected.value = next }
function selectListed(): void { selected.value = new Set([...selected.value, ...listed.value.map(keyOf)]) }
function clearListed(): void { const keys = new Set(listed.value.map(keyOf)); selected.value = new Set([...selected.value].filter((key) => !keys.has(key))) }
const selection = computed(() => all.value.filter((item) => selected.value.has(keyOf(item))))
const recipientCount = computed(() => selection.value.length)
const missingEmail = computed(() => selection.value.filter((item) => !item.displayEmail).length)
function toStep3(): void { if (!recipientCount.value) { ui.notify('Bitte wähle mindestens einen Empfänger aus.', 'warning'); return } step.value = 3 }

// ── Step 3 / 4: method and message ──────────────────────────────────────────
const method = ref<'simple' | 'relay'>('simple')
watch(relayEnabled.data, (enabled) => { if (!enabled) method.value = 'simple' })
const subject = ref(''); const html = ref(''); const busy = ref(false); const confirm = ref(false); const relay = ref<RelayResult | null>(null)
const validMessage = computed(() => !!subject.value.trim() && !!htmlToText(html.value))
function payload(): { recipientAssignmentIds: string[]; recipientUserIds: string[] } {
  return { recipientAssignmentIds: selection.value.filter((item) => item.isAssignmentRecipient).map((item) => item.assignmentId), recipientUserIds: selection.value.filter((item) => !item.isAssignmentRecipient).map((item) => item.userId) }
}
async function send(): Promise<void> {
  busy.value = true; confirm.value = false
  try {
    const result = await api.post<SendResult>('/api/email/bulk', { subject: subject.value.trim(), htmlBody: sanitizeEmailHtml(html.value), plainTextBody: htmlToText(html.value), ...payload() })
    if (result && result.success === false) throw new Error(result.errorMessage ?? '')
    ui.notify(`E-Mail wurde an ${result?.recipientCount ?? recipientCount.value} Empfänger gesendet.`, 'success')
    await router.push('/')
  } catch (reason) { ui.notify(reason instanceof Error && reason.message ? reason.message : 'Die E-Mail konnte nicht gesendet werden.', 'error') }
  finally { busy.value = false }
}
async function createRelay(): Promise<void> {
  busy.value = true
  try { relay.value = await api.post<RelayResult>('/api/email/relay/token', { ...payload(), subjectHint: subject.value || null }); ui.notify('E-Mail-Adresse wurde erzeugt.', 'success') }
  catch { ui.notify('E-Mail-Adresse konnte nicht erzeugt werden.', 'error') }
  finally { busy.value = false }
}
const formatMegabytes = (bytes: number): string => `${new Intl.NumberFormat('de-DE', { maximumFractionDigits: 1 }).format(bytes / 1024 / 1024)} MB`
async function copy(): Promise<void> {
  if (!relay.value) return
  try { await navigator.clipboard.writeText(relay.value.tokenAddress); ui.notify('Adresse wurde kopiert.', 'success') }
  catch { ui.notify('Die Adresse konnte nicht kopiert werden. Bitte markiere sie manuell.', 'warning') }
}
</script>

<template>
  <PageHeader title="E-Mail versenden" subtitle="Kontaktpersonen gesammelt per E-Mail erreichen" />
  <v-progress-linear v-if="candidates.isPending.value" indeterminate color="primary" />
  <v-alert v-else-if="candidates.isError.value" type="error">Kontakte konnten nicht geladen werden.</v-alert>
  <v-stepper v-else v-model="step" color="primary" :items="steps" alt-labels hide-actions>
    <!-- ═══ 1 Schnellauswahl ═══ -->
    <template #item.1>
      <v-card flat>
        <v-card-text>
          <v-alert type="info" variant="tonal" density="compact" class="mb-5">
            Die Filter dienen als Vorauswahl – im nächsten Schritt kannst du einzelne Personen an- oder abwählen. Du kannst auch ohne Filter weitergehen und Empfänger direkt suchen.
            <div class="mt-1"><strong>Filterlogik:</strong> Filtergruppen werden mit <strong>UND</strong> kombiniert, innerhalb einer Gruppe gilt <strong>ODER</strong>. Beispiel: Vereinstyp <em>Sportverein</em> + Position <em>Vorsitz</em> wählt nur Vorsitzende aus Sportvereinen.</div>
          </v-alert>

          <div v-if="clubTypes.data.value?.length" class="mb-5">
            <div class="text-title-small mb-2"><v-icon icon="mdi-shape-outline" size="small" class="mr-1" />Nach Vereinstyp</div>
            <v-chip-group v-model="filterClubTypes" multiple column selected-class="text-primary" filter>
              <v-chip v-for="type in clubTypes.data.value" :key="type.id" :value="type.id" variant="outlined">{{ type.name }} ({{ countBy((item) => item.clubTypeId === type.id) }})</v-chip>
            </v-chip-group>
          </div>
          <div v-if="positions.data.value?.length" class="mb-5">
            <div class="text-title-small mb-2"><v-icon icon="mdi-badge-account-outline" size="small" class="mr-1" />Nach Position</div>
            <v-chip-group v-model="filterPositions" multiple column selected-class="text-primary" filter>
              <v-chip v-for="position in positions.data.value" :key="position.id" :value="position.id" variant="outlined">{{ position.name }} ({{ countBy((item) => item.positionId === position.id) }})</v-chip>
            </v-chip-group>
          </div>
          <div v-if="locations.data.value?.length" class="mb-5" data-testid="location-filter">
            <div class="text-title-small mb-2"><v-icon icon="mdi-map-marker-radius-outline" size="small" class="mr-1" />Nach Standort</div>
            <v-chip-group v-model="filterLocations" multiple column selected-class="text-primary" filter>
              <v-chip v-for="location in locations.data.value" :key="location.id" :value="location.id" variant="outlined">{{ location.name }} ({{ organizationCountFor(location.id) }} Org.)</v-chip>
            </v-chip-group>
            <div class="text-body-small text-medium-emphasis">
              Wählt Vereine, Abteilungen und Gruppen, bei denen der Standort eingetragen ist (ohne Vererbung). Je Organisation erhalten die als „Erhält Standort-Infos“ markierten Kontaktpersonen die Mail, ist niemand markiert, alle Kontaktpersonen. Mit Positionsfilter entscheidet stattdessen die Position.
            </div>
          </div>
          <div class="mb-2">
            <div class="text-title-small mb-2"><v-icon icon="mdi-account-group-outline" size="small" class="mr-1" />Nach Verein (inkl. Abteilungen und Gruppen)</div>
            <v-autocomplete v-model="filterClubs" :items="clubOptions" item-title="title" item-value="value" label="Vereine auswählen" multiple chips closable-chips clearable hide-details>
              <template #item="{ props: itemProps, item }"><v-list-item v-bind="itemProps" :subtitle="`${item.count} Kontakte`" /></template>
            </v-autocomplete>
          </div>
        </v-card-text>
        <v-card-actions class="flex-wrap ga-2">
          <v-chip v-if="hasFilters" color="primary" variant="tonal">{{ filterMatches.length }} {{ filterMatches.length === 1 ? "Kontakt wird" : "Kontakte werden" }} vorausgewählt</v-chip>
          <v-btn v-if="hasFilters" variant="text" prepend-icon="mdi-filter-off" @click="resetFilters">Filter zurücksetzen</v-btn>
          <v-spacer />
          <v-btn color="primary" variant="flat" append-icon="mdi-arrow-right" @click="toStep2">{{ hasFilters ? 'Weiter' : 'Ohne Filter weiter' }}</v-btn>
        </v-card-actions>
      </v-card>
    </template>

    <!-- ═══ 2 Empfänger bestätigen ═══ -->
    <template #item.2>
      <v-card flat>
        <v-card-text>
          <v-alert v-if="!showAll && !search" type="info" variant="tonal" density="compact" class="mb-4">
            Angezeigt werden die Kontakte aus deiner Schnellauswahl. Bei einer Suche werden Treffer aus allen Kontakten angezeigt.
          </v-alert>
          <v-alert v-if="appliedLocationRule !== 'none'" type="info" variant="tonal" density="compact" icon="mdi-map-marker-radius-outline" class="mb-4" data-testid="location-rule-hint">
            <template v-if="appliedLocationRule === 'flags'">
              <strong>Standortfilter:</strong> Je Organisation sind die Kontaktpersonen mit „Erhält Standort-Infos“ ausgewählt. Ist bei einer Organisation niemand markiert, sind alle ihre Kontaktpersonen ausgewählt, damit die Info niemanden verfehlt.
            </template>
            <template v-else>
              <strong>Standortfilter mit Position:</strong> Ausgewählt sind die Kontaktpersonen mit der gewählten Position bei Organisationen, die den Standort nutzen. Die Markierung „Erhält Standort-Infos“ wird dabei nicht berücksichtigt.
            </template>
          </v-alert>
          <v-text-field v-model="search" label="Kontakte durchsuchen" placeholder="Name, E-Mail, Verein, Position …" prepend-inner-icon="mdi-magnify" clearable hide-details class="mb-3" />
          <div class="d-flex flex-wrap align-center ga-2 mb-3">
            <v-chip color="primary" variant="flat">{{ recipientCount }} von {{ all.length }} ausgewählt</v-chip>
            <v-btn size="small" variant="text" prepend-icon="mdi-checkbox-multiple-marked-outline" :disabled="!listed.length" @click="selectListed">Angezeigte auswählen</v-btn>
            <v-btn size="small" variant="text" prepend-icon="mdi-checkbox-multiple-blank-outline" :disabled="!listed.length" @click="clearListed">Angezeigte abwählen</v-btn>
            <v-spacer />
            <v-btn v-if="preselection.size && !search" size="small" variant="outlined" :prepend-icon="showAll ? 'mdi-filter' : 'mdi-eye'" @click="showAll = !showAll">{{ showAll ? 'Nur Vorauswahl anzeigen' : 'Alle Kontakte anzeigen' }}</v-btn>
          </div>
          <v-list v-if="listed.length" max-height="460" class="overflow-y-auto border rounded" lines="two">
            <v-list-item v-for="item in listed" :key="keyOf(item)" :active="isSelected(item)" color="primary" @click="toggle(item)">
              <template #prepend>
                <v-checkbox-btn :model-value="isSelected(item)" tabindex="-1" class="mr-1" @click.stop="toggle(item)" />
                <AvatarImage type="users" :id="item.userId" :has-avatar="item.hasAvatar" :initials="initialsOf(item.firstName, item.lastName)" :size="36" class="mr-3" />
              </template>
              <v-list-item-title class="d-flex align-center ga-2">
                {{ item.fullName ?? `${item.firstName} ${item.lastName}` }}
                <v-chip v-if="item.emailBadgeLabel" size="x-small" variant="outlined" color="info">{{ item.emailBadgeLabel }}</v-chip>
              </v-list-item-title>
              <v-list-item-subtitle>{{ [orgLabel(item), item.positionName, item.assignmentDetails].filter(Boolean).join(' · ') }}</v-list-item-subtitle>
              <template #append><span class="text-body-small text-medium-emphasis d-none d-md-inline">{{ item.displayEmail ?? 'Keine E-Mail sichtbar' }}</span></template>
            </v-list-item>
          </v-list>
          <v-empty-state v-else icon="mdi-account-search-outline" title="Keine Kontakte gefunden" :text="search ? 'Versuche einen anderen Suchbegriff.' : 'Suche nach Kontakten oder blende alle Kontakte ein.'" />
          <v-alert v-if="missingEmail" type="warning" variant="tonal" density="compact" class="mt-3">Für {{ missingEmail }} ausgewählte Kontakte ist keine E-Mail-Adresse sichtbar – sie erhalten die Nachricht trotzdem, sofern eine Adresse hinterlegt ist.</v-alert>
        </v-card-text>
        <v-card-actions><v-btn prepend-icon="mdi-arrow-left" @click="step = 1">Zurück</v-btn><v-spacer /><v-btn color="primary" variant="flat" append-icon="mdi-arrow-right" :disabled="!recipientCount" @click="toStep3">Weiter ({{ recipientCount }})</v-btn></v-card-actions>
      </v-card>
    </template>

    <!-- ═══ 3 Versandart ═══ -->
    <template #item.3>
      <v-card flat>
        <v-card-text>
          <p class="text-body-large mb-4">Wie möchtest du die E-Mail an {{ recipientCount }} Empfänger versenden?</p>
          <v-item-group v-model="method" mandatory>
            <v-row>
              <v-col cols="12" md="6">
                <v-item v-slot="{ isSelected: active, toggle: pick }" value="simple">
                  <v-card :color="active ? 'primary' : undefined" :variant="active ? 'tonal' : 'outlined'" height="100%" class="pa-4 text-center" @click="pick">
                    <v-icon icon="mdi-email-edit-outline" size="40" class="mb-2" />
                    <div class="text-title-large">Einfache Mail</div>
                    <p class="text-body-medium text-medium-emphasis mt-2 mb-0">Betreff und Text direkt hier eingeben und versenden. Ideal für einfache Nachrichten ohne Anhänge.</p>
                  </v-card>
                </v-item>
              </v-col>
              <v-col cols="12" md="6">
                <v-item v-slot="{ isSelected: active, toggle: pick }" value="relay" :disabled="!relayEnabled.data.value">
                  <v-card :color="active ? 'primary' : undefined" :variant="active ? 'tonal' : 'outlined'" height="100%" class="pa-4 text-center" :disabled="!relayEnabled.data.value" @click="pick">
                    <v-icon icon="mdi-email-fast-outline" size="40" class="mb-2" />
                    <div class="text-title-large">Eigener E-Mail-Client</div>
                    <p class="text-body-medium text-medium-emphasis mt-2 mb-0">{{ relayEnabled.data.value ? 'Temporäre Adresse erzeugen und aus deinem gewohnten Programm senden. Unterstützt Signatur, Anhänge und Formatierung.' : 'Der E-Mail-Relay-Dienst ist nicht konfiguriert.' }}</p>
                  </v-card>
                </v-item>
              </v-col>
            </v-row>
          </v-item-group>
        </v-card-text>
        <v-card-actions><v-btn prepend-icon="mdi-arrow-left" @click="step = 2">Zurück</v-btn><v-spacer /><v-btn color="primary" variant="flat" append-icon="mdi-arrow-right" @click="step = 4">Weiter</v-btn></v-card-actions>
      </v-card>
    </template>

    <!-- ═══ 4 Verfassen & Senden ═══ -->
    <template #item.4>
      <v-card flat>
        <template v-if="method === 'simple'">
          <v-form @submit.prevent="validMessage && (confirm = true)">
            <v-card-text>
              <v-alert type="info" variant="tonal" density="compact" class="mb-4">Die E-Mail wird an {{ recipientCount }} Empfänger als BCC gesendet. Empfänger sehen die Adressen der anderen nicht, aber deine E-Mail-Adresse, damit sie direkt antworten können.</v-alert>
              <v-text-field v-model="subject" label="Betreff *" maxlength="200" counter />
              <div class="text-title-small mb-2">Nachricht *</div>
              <RichTextEditor v-model="html" />
            </v-card-text>
            <v-card-actions><v-btn prepend-icon="mdi-arrow-left" @click="step = 3">Zurück</v-btn><v-spacer /><v-btn type="submit" color="primary" variant="flat" prepend-icon="mdi-send" :disabled="!validMessage" :loading="busy">E-Mail senden ({{ recipientCount }})</v-btn></v-card-actions>
          </v-form>
        </template>
        <template v-else>
          <v-card-text>
            <template v-if="!relay">
              <v-alert type="info" variant="tonal" density="compact" class="mb-4">Erzeuge eine temporäre E-Mail-Adresse und schreibe deine Nachricht in deinem gewohnten E-Mail-Programm an diese Adresse. Sie wird automatisch an alle {{ recipientCount }} Empfänger weitergeleitet.</v-alert>
              <v-text-field v-model="subject" label="Betreffhinweis (optional)" maxlength="200" />
              <v-btn color="primary" block size="large" prepend-icon="mdi-at" :loading="busy" @click="createRelay">E-Mail-Adresse erzeugen</v-btn>
            </template>
            <v-alert v-else type="success" variant="tonal">
              <div class="text-title-large">Adresse erzeugt</div>
              <div class="text-headline-small my-3 text-break">{{ relay.tokenAddress }}</div>
              <v-btn color="primary" prepend-icon="mdi-content-copy" @click="copy">Adresse kopieren</v-btn>
              <div class="mt-3">Gültig bis {{ formatDateTime(relay.expiresAt) }} · {{ relay.recipientCount }} Empfänger</div>
              <div v-if="relay.maxMessageSizeBytes" class="mt-1">Maximale Größe der E-Mail: {{ formatMegabytes(relay.maxMessageSizeBytes) }} inklusive Anhängen (Anhänge werden beim Versand etwa ein Drittel größer, praktisch sind das rund {{ formatMegabytes(relay.maxMessageSizeBytes * 0.75) }} Anhänge). Zu große E-Mails werden nicht weitergeleitet, du bekommst dann eine Benachrichtigung.</div>
            </v-alert>
          </v-card-text>
          <v-card-actions><v-btn v-if="!relay" prepend-icon="mdi-arrow-left" @click="step = 3">Zurück</v-btn><v-spacer /><v-btn v-if="relay" color="primary" variant="flat" to="/">Fertig</v-btn></v-card-actions>
        </template>
      </v-card>
    </template>
  </v-stepper>
  <ConfirmDialog v-model="confirm" title="E-Mail senden?" :text="`Diese E-Mail jetzt an ${recipientCount} Empfänger senden?`" confirm-text="Senden" confirm-color="primary" @confirm="send" />
</template>
