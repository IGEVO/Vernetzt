<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import CalendarFeedsPanel from '@/components/CalendarFeedsPanel.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import EventCalendarGrid from '@/components/EventCalendarGrid.vue'
import { useCalendarHolidays } from '@/composables/useCalendarHolidays'
import { organizationPath } from '@/services/organizationPath'
import EventDetailDialog from '@/components/EventDetailDialog.vue'
import EventImage from '@/components/EventImage.vue'
import EventThumb from '@/components/EventThumb.vue'
import FocalPointDialog from '@/components/FocalPointDialog.vue'
import RichTextEditor from '@/components/RichTextEditor.vue'
import { ApiError, api, toQuery } from '@/services/api'
import {
  MEDIA_VIEWS,
  MIN_RECOMMENDED_WIDTH,
  formatFileSize,
  hasPreview,
  isCover,
  isPoster,
  mediaIcon,
  mediaTypeLabel,
  readImageSize,
  variantUrl
} from '@/services/eventMedia'
import { allDayInclusiveEnd, eventRangeToUtc, utcToLocalDate, utcToLocalInput } from '@/services/dates'
import { useUiStore } from '@/stores/ui'
import type { CalendarFeedTokenDto, ClubDto, DepartmentDto, EventDto, EventMediaDto, PagedResult } from '@/types/api'

const props = defineProps<{ entityType: 'club' | 'department' }>()

const route = useRoute()
const ui = useUiStore()
const queryClient = useQueryClient()

const id = computed(() => String(route.params.id))

/** Events of departments (club view) or groups (department view) name their organizer, e.g. "Handball › Bambini". */
function subUnitLabel(item: EventDto): string | null {
  if (!item.departmentId || item.departmentId === id.value) return null
  return props.entityType === 'club'
    ? organizationPath({ parentDepartmentName: item.parentDepartmentName, departmentName: item.departmentName })
    : item.departmentName ?? null
}
const mainTab = ref<'calendar' | 'list' | 'ical'>('calendar')
const showPast = ref(false)
const page = ref(1)

// ─── Dialogs ────────────────────────────────────────────────────────────────
const dialog = ref(false)
const deleting = ref<EventDto | null>(null)
const detail = ref<EventDto | null>(null)
const editing = ref<EventDto | null>(null)
const deletingMedia = ref<EventMediaDto | null>(null)

// ─── Form state ─────────────────────────────────────────────────────────────
const form = reactive({
  title: '',
  description: '',
  location: '',
  start: '',
  end: '',
  isAllDay: false,
  isHighlight: false
})

// ─── Media state ────────────────────────────────────────────────────────────
const MAX_MEDIA = 10

/** Files queued for upload when creating/editing. */
interface PendingFile {
  key: number
  file: File
  type: 0 | 1 | 2 | 3
  /** Object URL for local preview (null for formats the browser cannot show, e.g. HEIC) */
  preview: string | null
  focusX: number
  focusY: number
  width?: number
  height?: number
  /** Explicit view assignments (bit flags), sent with the upload */
  usages: number
  /** queued: uploaded on save (new event) · uploading · error */
  status: 'queued' | 'uploading' | 'error'
}
const pendingFiles = ref<PendingFile[]>([])
/** Already uploaded media of the event being edited (changes are saved immediately). */
const existingMedia = ref<EventMediaDto[]>([])
const mediaChanged = ref(false)
const uploadLoading = ref(false)
const pendingInputRef = ref<HTMLInputElement>()
let pendingKey = 0

const uploading = computed(() => pendingFiles.value.some((p) => p.status === 'uploading'))
const mediaCount = computed(() => existingMedia.value.length + pendingFiles.value.length)
const hasAnyCover = computed(() =>
  existingMedia.value.some((m) => isCover(m.type)) || pendingFiles.value.some((p) => p.type === 0)
)

/** Focal point dialog target */
const focal = ref<{ src: string; focusX: number; focusY: number; apply: (x: number, y: number) => void } | null>(null)

function triggerPendingInput(): void {
  if (pendingInputRef.value) {
    pendingInputRef.value.value = ''
    pendingInputRef.value.click()
  }
}

function onPendingFileSelected(e: Event): void {
  const input = e.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  if (files.length) void addPendingFiles(files)
}

const entity = useQuery({
  queryKey: computed(() => [props.entityType, id.value]),
  queryFn: ({ signal }) =>
    api.get<ClubDto | DepartmentDto>(
      `/api/${props.entityType === 'club' ? 'clubs' : 'departments'}/${id.value}`,
      signal
    )
})

const { holidays, onViewMonthChange } = useCalendarHolidays()

const now = new Date()
const fromUtc = computed(() => (showPast.value ? undefined : now.toISOString()))

const eventsPaged = useQuery({
  queryKey: computed(() => ['events', props.entityType, id.value, page.value, showPast.value]),
  queryFn: ({ signal }) =>
    api.get<PagedResult<EventDto>>(
      `/api/events/${props.entityType}/${id.value}/paged${toQuery({
        page: page.value,
        pageSize: 20,
        includeDepartments: props.entityType === 'club',
        fromUtc: fromUtc.value
      })}`,
      signal
    )
})

const eventsAll = useQuery({
  queryKey: computed(() => ['events-all', props.entityType, id.value]),
  queryFn: ({ signal }) =>
    api.get<EventDto[]>(
      `/api/events/${props.entityType}/${id.value}${toQuery({
        includeDepartments: props.entityType === 'club'
      })}`,
      signal
    ),
  enabled: computed(() => mainTab.value === 'calendar')
})

const canEdit = computed(() => entity.data.value?.canEdit ?? false)

// Same query as in CalendarFeedsPanel (shared cache); read-only users only receive existing public links.
const feeds = useQuery({
  queryKey: ['calendar-feeds', props.entityType, id],
  queryFn: ({ signal }) =>
    api.get<CalendarFeedTokenDto[]>(
      `/api/calendar-feeds/tokens${toQuery(props.entityType === 'club' ? { clubId: id.value } : { departmentId: id.value })}`,
      signal
    )
})
const hasFeeds = computed(() => (feeds.data.value ?? []).some((t) => !t.isRevoked && !!t.feedUrl))
const showIcal = computed(() => canEdit.value || hasFeeds.value)

// reset page when filter changes
watch(showPast, () => { page.value = 1 })

watch(
  () => form.isAllDay,
  (allDay) => {
    if (allDay) {
      form.start = form.start.slice(0, 10)
      form.end = form.end.slice(0, 10)
    } else {
      if (form.start && !form.start.includes('T')) form.start += 'T09:00'
      if (form.end && !form.end.includes('T')) form.end += 'T10:00'
    }
  }
)

// ─── Form helpers ────────────────────────────────────────────────────────────
function openNew(prefillDate?: string): void {
  editing.value = null
  resetMediaState([])
  Object.assign(form, {
    title: '',
    description: '',
    location: '',
    start: prefillDate ? `${prefillDate}T09:00` : '',
    end: prefillDate ? `${prefillDate}T10:00` : '',
    isAllDay: false,
    isHighlight: false
  })
  dialog.value = true
}

function openEdit(item: EventDto): void {
  editing.value = item
  resetMediaState(item.media ?? [])
  Object.assign(form, {
    title: item.title,
    description: item.description ?? '',
    location: item.location ?? '',
    start: item.isAllDay ? utcToLocalDate(item.startUtc) : utcToLocalInput(item.startUtc),
    end: item.isAllDay ? allDayInclusiveEnd(item.endUtc) : utcToLocalInput(item.endUtc),
    isAllDay: item.isAllDay,
    isHighlight: item.isHighlight
  })
  dialog.value = true
}

// ─── Media management ───────────────────────────────────────────────────────
function resetMediaState(media: EventMediaDto[]): void {
  for (const p of pendingFiles.value) if (p.preview) URL.revokeObjectURL(p.preview)
  pendingFiles.value = []
  existingMedia.value = media.map((m) => ({ ...m }))
  mediaChanged.value = false
}

function isImageCandidate(file: File): boolean {
  return file.type.startsWith('image/') || /\.(heic|heif)$/i.test(file.name)
}

async function addPendingFiles(files: File[]): Promise<void> {
  for (const file of files) {
    if (mediaCount.value >= MAX_MEDIA) {
      ui.notify(`Ein Termin kann höchstens ${MAX_MEDIA} Dateien haben.`, 'warning')
      break
    }
    if (file.size > 15 * 1024 * 1024) {
      ui.notify(`„${file.name}“ ist größer als 15 MB.`, 'warning')
      continue
    }
    const isImg = isImageCandidate(file)
    const size = isImg ? await readImageSize(file) : null
    const item: PendingFile = {
      key: ++pendingKey,
      file,
      // The first image automatically becomes the title image – any format is fine.
      type: isImg ? (hasAnyCover.value ? 3 : 0) : 2,
      preview: size ? URL.createObjectURL(file) : null,
      focusX: 0.5,
      focusY: 0.5,
      width: size?.width,
      height: size?.height,
      usages: 0,
      status: 'queued'
    }
    pendingFiles.value.push(item)
    // Existing events: upload right away. New events: upload after the event has been created.
    if (editing.value) void uploadPending(pendingFiles.value[pendingFiles.value.length - 1]!, editing.value.id)
  }
}

/** Uploads one queued file; on success it moves to the existing media. Returns false on error. */
async function uploadPending(item: PendingFile, eventId: string): Promise<boolean> {
  item.status = 'uploading'
  const fd = new FormData()
  fd.append('file', item.file)
  fd.append('type', String(item.type))
  fd.append('focusX', String(item.focusX))
  fd.append('focusY', String(item.focusY))
  fd.append('usages', String(item.usages))
  try {
    const uploaded = await api.postForm<EventMediaDto>(`/api/events/${eventId}/media`, fd)
    if (isCover(uploaded.type)) for (const m of existingMedia.value) if (isCover(m.type)) m.type = 3
    for (const m of existingMedia.value) m.usages &= ~uploaded.usages
    if (isPoster(uploaded.type)) demotePosters(uploaded.id)
    existingMedia.value.push(uploaded)
    removePending(item.key)
    mediaChanged.value = true
    return true
  } catch (error) {
    item.status = 'error'
    const reason = error instanceof ApiError ? error.message : 'Unbekannter Fehler'
    ui.notify(`„${item.file.name}“ konnte nicht hochgeladen werden: ${reason}`, 'error')
    return false
  }
}

function retryPending(item: PendingFile): void {
  if (editing.value) void uploadPending(item, editing.value.id)
}

function removePending(key: number): void {
  const idx = pendingFiles.value.findIndex((p) => p.key === key)
  const item = pendingFiles.value[idx]
  if (!item) return
  if (item.preview) URL.revokeObjectURL(item.preview)
  pendingFiles.value.splice(idx, 1)
}

function setPendingCover(item: PendingFile): void {
  for (const p of pendingFiles.value) if (p.type === 0) p.type = 3
  // The server demotes the existing cover on upload; mirror that locally.
  for (const m of existingMedia.value) if (isCover(m.type)) m.type = 3
  item.type = 0
}

/** At most one poster per event: all other posters become regular images (the server does the same). */
function demotePosters(exceptId?: string, exceptKey?: number): void {
  for (const m of existingMedia.value) if (m.id !== exceptId && isPoster(m.type)) m.type = 3
  for (const p of pendingFiles.value) if (p.key !== exceptKey && p.type === 1) p.type = 3
}

/** Marks the file as poster; clicking an existing poster turns it back into a regular image. */
function togglePendingPoster(item: PendingFile): void {
  if (item.type === 1) {
    item.type = 3
    return
  }
  demotePosters(undefined, item.key)
  item.type = 1
}

async function toggleExistingPoster(media: EventMediaDto): Promise<void> {
  if (!editing.value) return
  const next = isPoster(media.type) ? 3 : 1
  try {
    await api.put(`/api/events/${editing.value.id}/media/${media.id}`, { type: next })
    if (next === 1) demotePosters(media.id)
    media.type = next
    mediaChanged.value = true
  } catch (error) {
    ui.notify(error instanceof ApiError ? error.message : 'Plakat konnte nicht gesetzt werden.', 'error')
  }
}

async function setExistingCover(media: EventMediaDto): Promise<void> {
  if (!editing.value) return
  try {
    await api.put(`/api/events/${editing.value.id}/media/${media.id}`, { type: 0 })
    for (const m of existingMedia.value) if (isCover(m.type)) m.type = 3
    for (const p of pendingFiles.value) if (p.type === 0) p.type = 3
    media.type = 0
    mediaChanged.value = true
  } catch (error) {
    ui.notify(error instanceof ApiError ? error.message : 'Titelbild konnte nicht gesetzt werden.', 'error')
  }
}

function editPendingFocus(item: PendingFile): void {
  if (!item.preview) return
  focal.value = {
    src: item.preview,
    focusX: item.focusX,
    focusY: item.focusY,
    apply: (x, y) => { item.focusX = x; item.focusY = y }
  }
}

function editExistingFocus(media: EventMediaDto): void {
  if (!editing.value || !hasPreview(media)) return
  const eventId = editing.value.id
  focal.value = {
    src: variantUrl(media, 960),
    focusX: media.focusX,
    focusY: media.focusY,
    apply: async (x, y) => {
      try {
        await api.put(`/api/events/${eventId}/media/${media.id}`, { focusX: x, focusY: y })
        media.focusX = x
        media.focusY = y
        mediaChanged.value = true
      } catch (error) {
        ui.notify(error instanceof ApiError ? error.message : 'Bildausschnitt konnte nicht gespeichert werden.', 'error')
      }
    }
  }
}

async function removeExisting(): Promise<void> {
  const media = deletingMedia.value
  if (!media || !editing.value) return
  try {
    await api.delete(`/api/events/${editing.value.id}/media/${media.id}`)
    existingMedia.value = existingMedia.value.filter((m) => m.id !== media.id)
    mediaChanged.value = true
  } catch (error) {
    ui.notify(error instanceof ApiError ? error.message : 'Datei konnte nicht gelöscht werden.', 'error')
  } finally {
    deletingMedia.value = null
  }
}

// ─── View assignment ────────────────────────────────────────────────────────
/** Images that can be assigned to a view: uploaded ones with preview and queued ones the browser can show. */
interface ViewCandidate {
  value: string
  title: string
  media?: EventMediaDto
  pending?: PendingFile
}

const viewCandidates = computed<ViewCandidate[]>(() => [
  ...existingMedia.value
    .filter((m) => hasPreview(m))
    .map((m) => ({ value: `m:${m.id}`, title: m.fileName, media: m })),
  ...pendingFiles.value
    .filter((p) => p.preview && p.status !== 'uploading')
    .map((p) => ({ value: `p:${p.key}`, title: p.file.name, pending: p }))
])

/** Image used for views without explicit assignment (mirrors the server-side fallback). */
const defaultCandidate = computed<ViewCandidate | undefined>(() =>
  viewCandidates.value.find((c) => (c.media ? isCover(c.media.type) : c.pending?.type === 0))
  ?? viewCandidates.value.find((c) => c.media && (c.media.type === 3 || c.media.type === 'Image'))
  ?? viewCandidates.value.find((c) => c.media)
  ?? viewCandidates.value[0]
)

/** Select value for "no explicit assignment" (Vuetify does not handle null item values well). */
const VIEW_DEFAULT = 'default'

function viewValue(usage: number): string {
  return viewCandidates.value.find((c) => ((c.media?.usages ?? c.pending?.usages ?? 0) & usage) !== 0)?.value ?? VIEW_DEFAULT
}

function viewCandidate(usage: number): ViewCandidate | undefined {
  const value = viewValue(usage)
  return value !== VIEW_DEFAULT ? viewCandidates.value.find((c) => c.value === value) : defaultCandidate.value
}

async function assignView(usage: number, value: string): Promise<void> {
  const target = viewCandidates.value.find((c) => c.value === value)
  for (const p of pendingFiles.value) if (p !== target?.pending) p.usages &= ~usage
  try {
    if (target?.media && editing.value) {
      // The server removes the view from the previous holder.
      await api.put(`/api/events/${editing.value.id}/media/${target.media.id}`, { usages: target.media.usages | usage })
    } else if (editing.value) {
      const holder = existingMedia.value.find((m) => (m.usages & usage) !== 0)
      if (holder) await api.put(`/api/events/${editing.value.id}/media/${holder.id}`, { usages: holder.usages & ~usage })
    }
    for (const m of existingMedia.value) if (m !== target?.media) m.usages &= ~usage
    if (target?.media) target.media.usages |= usage
    if (target?.pending) target.pending.usages |= usage
    if (editing.value) mediaChanged.value = true
  } catch (error) {
    ui.notify(error instanceof ApiError ? error.message : 'Zuordnung konnte nicht gespeichert werden.', 'error')
  }
}

function lowResolution(width: number | null | undefined): boolean {
  return !!width && width < MIN_RECOMMENDED_WIDTH
}

async function closeDialog(): Promise<void> {
  dialog.value = false
  if (mediaChanged.value) await invalidateEvents()
  resetMediaState([])
}

// ─── Save / Delete ────────────────────────────────────────────────────────────
async function save(): Promise<void> {
  const range = eventRangeToUtc(form.start, form.end, form.isAllDay)
  const body = {
    title: form.title,
    description: form.description || null,
    location: form.location || null,
    ...range,
    isAllDay: form.isAllDay,
    isHighlight: form.isHighlight,
    ...(props.entityType === 'club' ? { clubId: id.value } : { departmentId: id.value })
  }
  uploadLoading.value = true
  try {
    let eventId: string
    if (editing.value) {
      await api.put(`/api/events/${editing.value.id}`, body)
      eventId = editing.value.id
    } else {
      const created = await api.post<EventDto>('/api/events', body)
      eventId = created.id
      // Keep the created event so that a retry after a failed upload does not create a duplicate.
      editing.value = created
    }

    // Upload files that are still queued (new event) or failed before
    for (const pending of [...pendingFiles.value]) {
      if (pending.status === 'uploading') continue
      if (!await uploadPending(pending, eventId)) return
    }
    mediaChanged.value = true
    await closeDialog()
    ui.notify('Termin wurde gespeichert.', 'success')
  } catch (error) {
    ui.notify(error instanceof ApiError ? error.message : 'Termin konnte nicht gespeichert werden.', 'error')
  } finally {
    uploadLoading.value = false
  }
}

async function remove(): Promise<void> {
  if (!deleting.value) return
  try {
    await api.delete(`/api/events/${deleting.value.id}`)
    deleting.value = null
    await invalidateEvents()
    ui.notify('Termin wurde gelöscht.', 'success')
  } catch {
    ui.notify('Termin konnte nicht gelöscht werden.', 'error')
  }
}

async function invalidateEvents(): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: ['events', props.entityType, id.value] })
  await queryClient.invalidateQueries({ queryKey: ['events-all', props.entityType, id.value] })
}

// ─── Formatting ──────────────────────────────────────────────────────────────
const dateFormatter = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' })
const dateOnlyFormatter = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })

function formatEvent(item: EventDto): string {
  if (item.isAllDay) {
    const start = dateOnlyFormatter.format(new Date(item.startUtc))
    const endExcl = new Date(item.endUtc)
    endExcl.setDate(endExcl.getDate() - 1)
    const end = dateOnlyFormatter.format(endExcl)
    return start === end ? start : `${start} – ${end}`
  }
  return `${dateFormatter.format(new Date(item.startUtc))} – ${dateFormatter.format(new Date(item.endUtc))}`
}

</script>

<template>
  <!-- ── Main tabs ────────────────────────────────────────────────────────── -->
  <div class="d-flex align-center mb-1 flex-wrap gap-3">
    <h2 class="text-headline-small">Kalender & Termine</h2>
    <v-spacer />
    <v-btn v-if="canEdit && (mainTab === 'calendar' || mainTab === 'list')" color="primary" prepend-icon="mdi-plus" @click="openNew()">
      Termin anlegen
    </v-btn>
  </div>

  <v-tabs v-model="mainTab" color="primary" class="mb-4">
    <v-tab value="calendar" prepend-icon="mdi-calendar-month">Kalender</v-tab>
    <v-tab value="list" prepend-icon="mdi-format-list-bulleted">Liste</v-tab>
    <v-tab v-if="showIcal" value="ical" prepend-icon="mdi-calendar-sync">iCal-Abos</v-tab>
  </v-tabs>

  <!-- ═══ CALENDAR TAB ════════════════════════════════════════════════════ -->
  <template v-if="mainTab === 'calendar'">
    <v-progress-linear v-if="eventsAll.isPending.value" indeterminate />
    <v-alert v-else-if="eventsAll.isError.value" type="error">Termine konnten nicht geladen werden.</v-alert>
    <EventCalendarGrid
      v-else
      :events="eventsAll.data.value ?? []"
      :holidays="holidays"
      :can-edit="canEdit"
      @click-event="detail = $event"
      @new-event="openNew($event)"
      @view-month-change="onViewMonthChange"
    />
  </template>

  <!-- ═══ LIST TAB ════════════════════════════════════════════════════════ -->
  <template v-else-if="mainTab === 'list'">
    <div class="d-flex align-center mb-3">
      <v-spacer />
      <v-switch
        v-model="showPast"
        label="Vergangene anzeigen"
        density="compact"
        hide-details
        color="secondary"
      />
    </div>
    <v-progress-linear v-if="eventsPaged.isPending.value" indeterminate />
    <v-alert v-else-if="eventsPaged.isError.value" type="error">Termine konnten nicht geladen werden.</v-alert>
    <v-empty-state
      v-else-if="!eventsPaged.data.value?.items.length"
      :title="showPast ? 'Keine Termine' : 'Keine anstehenden Termine'"
      icon="mdi-calendar-blank"
    />
    <v-list v-else lines="two" class="py-0">
      <v-list-item
        v-for="item in eventsPaged.data.value?.items"
        :key="item.id"
        class="border rounded mb-2 cursor-pointer mx-1"
        @click="detail = item"
      >
        <template #prepend>
          <EventThumb :event="item" class="mr-4" />
        </template>
        <v-list-item-title class="font-weight-medium">{{ item.title }}</v-list-item-title>
        <v-list-item-subtitle>
          {{ formatEvent(item) }}
          <template v-if="item.location"> · <v-icon icon="mdi-map-marker" size="12" />{{ item.location }}</template>
          <v-chip v-if="subUnitLabel(item)" size="x-small" variant="tonal" prepend-icon="mdi-source-branch" class="ml-2">{{ subUnitLabel(item) }}</v-chip>
          <v-chip v-if="item.media?.length" size="x-small" variant="tonal" class="ml-1">
            <v-icon icon="mdi-paperclip" size="12" start />{{ item.media.length }}
          </v-chip>
        </v-list-item-subtitle>
        <template v-if="canEdit" #append>
          <v-btn icon="mdi-pencil" variant="text" density="comfortable" aria-label="Bearbeiten" @click.stop="openEdit(item)" />
          <v-btn icon="mdi-delete" color="error" variant="text" density="comfortable" aria-label="Löschen" @click.stop="deleting = item" />
        </template>
      </v-list-item>
    </v-list>
    <v-pagination
      v-if="(eventsPaged.data.value?.totalPages ?? 0) > 1"
      v-model="page"
      :length="eventsPaged.data.value?.totalPages ?? 1"
      class="mt-2"
    />
  </template>

  <!-- ═══ iCAL TAB ════════════════════════════════════════════════════════ -->
  <template v-else-if="mainTab === 'ical' && showIcal">
    <CalendarFeedsPanel :entity-type="props.entityType" :entity-id="id" :readonly="!canEdit" />
  </template>

  <EventDetailDialog
    :model-value="!!detail"
    :event="detail"
    :can-edit="canEdit"
    @update:model-value="!$event && (detail = null)"
    @edit="openEdit($event); detail = null"
  />

  <!-- ═══ CREATE / EDIT DIALOG ════════════════════════════════════════════ -->
  <v-dialog v-model="dialog" max-width="700" persistent scrollable>
    <v-card>
      <v-card-title class="pt-4 px-6">{{ editing ? 'Termin bearbeiten' : 'Termin anlegen' }}</v-card-title>
      <v-card-text class="px-6">
        <v-alert type="info" variant="tonal" density="compact" class="mb-4">
          Alle Termine sind <strong>öffentlich sichtbar</strong>, auch im öffentlichen Portal und in Kalender-Abos.
        </v-alert>
        <v-text-field v-model="form.title" label="Titel *" />
        <v-checkbox v-model="form.isAllDay" label="Ganztägig" density="comfortable" />
        <v-row>
          <v-col cols="12" sm="6">
            <v-text-field v-model="form.start" label="Beginn *" :type="form.isAllDay ? 'date' : 'datetime-local'" />
          </v-col>
          <v-col cols="12" sm="6">
            <v-text-field v-model="form.end" label="Ende *" :type="form.isAllDay ? 'date' : 'datetime-local'" />
          </v-col>
        </v-row>
        <v-text-field v-model="form.location" label="Ort" prepend-inner-icon="mdi-map-marker" />
        <v-checkbox
          v-model="form.isHighlight"
          label="Highlight"
          hint="Highlights werden im Hauptkalender und im öffentlichen Portal hervorgehoben."
          persistent-hint
          density="comfortable"
          class="mb-3"
        />
        <div class="text-title-small mb-1">Beschreibung</div>
        <RichTextEditor v-model="form.description" />

        <!-- Media -->
        <div class="mt-5">
          <div class="d-flex align-center mb-1">
            <span class="text-title-small">Bilder & Dokumente</span>
            <span class="text-body-small text-medium-emphasis ml-2">{{ mediaCount }}/{{ MAX_MEDIA }}</span>
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-upload"
              :disabled="mediaCount >= MAX_MEDIA"
              @click="triggerPendingInput"
            >
              Dateien hinzufügen
            </v-btn>
          </div>
          <p class="text-body-small text-medium-emphasis mb-3">
            Titelbild mit <v-icon icon="mdi-star-outline" size="12" /> wählen, Plakat mit
            <v-icon icon="mdi-image-text" size="12" /> (wird unbeschnitten im A4-Format gezeigt), Ausschnitt mit
            <v-icon icon="mdi-image-filter-center-focus" size="12" /> anpassen.
          </p>

          <div v-if="mediaCount" class="media-grid">
            <!-- already uploaded -->
            <div v-for="m in existingMedia" :key="m.id" class="media-card" :class="{ 'media-card--cover': isCover(m.type), 'media-card--poster': isPoster(m.type) }">
              <EventImage v-if="hasPreview(m)" :media="m" :aspect="1" sizes="120px" />
              <div v-else class="media-card__icon"><v-icon :icon="mediaIcon(m)" size="40" /></div>
              <div class="media-card__meta">
                <div class="text-body-small text-truncate" :title="m.fileName">{{ m.fileName }}</div>
                <div class="text-body-small text-medium-emphasis">
                  {{ mediaTypeLabel(m.type) }} · {{ formatFileSize(m.fileSize) }}
                </div>
                <v-chip v-if="lowResolution(m.width) && hasPreview(m)" size="x-small" color="warning" variant="tonal" class="mt-1">
                  geringe Auflösung
                </v-chip>
              </div>
              <div class="media-card__actions">
                <v-btn
                  v-if="hasPreview(m)"
                  :icon="isCover(m.type) ? 'mdi-star' : 'mdi-star-outline'"
                  :color="isCover(m.type) ? 'secondary' : undefined"
                  size="x-small"
                  variant="text"
                  :aria-label="isCover(m.type) ? 'Ist Titelbild' : 'Als Titelbild verwenden'"
                  :title="isCover(m.type) ? 'Ist Titelbild' : 'Als Titelbild verwenden'"
                  @click="!isCover(m.type) && setExistingCover(m)"
                />
                <v-btn
                  v-if="hasPreview(m)"
                  icon="mdi-image-text"
                  :color="isPoster(m.type) ? 'secondary' : undefined"
                  size="x-small"
                  variant="text"
                  :aria-label="isPoster(m.type) ? 'Plakat entfernen' : 'Als Plakat verwenden'"
                  :title="isPoster(m.type) ? 'Plakat entfernen' : 'Als Plakat verwenden'"
                  @click="toggleExistingPoster(m)"
                />
                <v-btn
                  v-if="hasPreview(m) && !isPoster(m.type)"
                  icon="mdi-image-filter-center-focus"
                  size="x-small"
                  variant="text"
                  aria-label="Bildausschnitt festlegen"
                  title="Bildausschnitt festlegen"
                  @click="editExistingFocus(m)"
                />
                <v-btn
                  icon="mdi-delete-outline"
                  size="x-small"
                  variant="text"
                  color="error"
                  aria-label="Datei löschen"
                  title="Datei löschen"
                  @click="deletingMedia = m"
                />
              </div>
            </div>

            <!-- queued for upload -->
            <div v-for="pf in pendingFiles" :key="pf.key" class="media-card media-card--pending" :class="{ 'media-card--cover': pf.type === 0, 'media-card--poster': pf.type === 1 }">
              <div v-if="pf.status === 'uploading'" class="media-card__overlay">
                <v-progress-circular indeterminate color="white" size="32" width="3" />
              </div>
              <div v-if="pf.preview" class="media-card__preview">
                <img :src="pf.preview" alt="" :style="{ objectPosition: `${pf.focusX * 100}% ${pf.focusY * 100}%` }">
              </div>
              <div v-else class="media-card__icon">
                <v-icon icon="mdi-image" size="40" />
              </div>
              <div class="media-card__meta">
                <div class="text-body-small text-truncate" :title="pf.file.name">{{ pf.file.name }}</div>
                <div class="text-body-small text-medium-emphasis">
                  {{ mediaTypeLabel(pf.type) }} · {{ formatFileSize(pf.file.size) }}
                </div>
                <v-chip v-if="pf.status === 'error'" size="x-small" color="error" variant="tonal" class="mt-1">
                  Fehlgeschlagen
                </v-chip>
                <v-chip v-if="lowResolution(pf.width)" size="x-small" color="warning" variant="tonal" class="mt-1">
                  geringe Auflösung
                </v-chip>
              </div>
              <div v-if="pf.status === 'error' && editing" class="media-card__actions">
                <v-btn icon="mdi-refresh" size="x-small" variant="text" aria-label="Erneut versuchen" title="Erneut versuchen" @click="retryPending(pf)" />
                <v-btn icon="mdi-close" size="x-small" variant="text" color="error" aria-label="Entfernen" title="Entfernen" @click="removePending(pf.key)" />
              </div>
              <div v-else-if="pf.status !== 'uploading'" class="media-card__actions">
                <v-btn
                  v-if="pf.type !== 2"
                  :icon="pf.type === 0 ? 'mdi-star' : 'mdi-star-outline'"
                  :color="pf.type === 0 ? 'secondary' : undefined"
                  size="x-small"
                  variant="text"
                  :aria-label="pf.type === 0 ? 'Ist Titelbild' : 'Als Titelbild verwenden'"
                  :title="pf.type === 0 ? 'Ist Titelbild' : 'Als Titelbild verwenden'"
                  @click="setPendingCover(pf)"
                />
                <v-btn
                  v-if="pf.type !== 2"
                  icon="mdi-image-text"
                  :color="pf.type === 1 ? 'secondary' : undefined"
                  size="x-small"
                  variant="text"
                  :aria-label="pf.type === 1 ? 'Plakat entfernen' : 'Als Plakat verwenden'"
                  :title="pf.type === 1 ? 'Plakat entfernen' : 'Als Plakat verwenden'"
                  @click="togglePendingPoster(pf)"
                />
                <v-btn
                  v-if="pf.preview && pf.type !== 1"
                  icon="mdi-image-filter-center-focus"
                  size="x-small"
                  variant="text"
                  aria-label="Bildausschnitt festlegen"
                  title="Bildausschnitt festlegen"
                  @click="editPendingFocus(pf)"
                />
                <v-btn
                  icon="mdi-close"
                  size="x-small"
                  variant="text"
                  color="error"
                  aria-label="Entfernen"
                  title="Entfernen"
                  @click="removePending(pf.key)"
                />
              </div>
            </div>
          </div>
          <p v-else class="text-body-small text-medium-emphasis">Noch keine Dateien hinzugefügt.</p>

          <div v-if="viewCandidates.length > 1" class="mt-4">
            <div class="text-title-small mb-2">Bild je Ansicht</div>
            <div v-for="view in MEDIA_VIEWS" :key="view.usage" class="view-row">
              <div class="view-row__preview" :style="{ width: `${Math.round(48 * view.aspect)}px` }">
                <template v-for="c in [viewCandidate(view.usage)]" :key="c?.value ?? 'none'">
                  <EventImage v-if="c?.media" :media="c.media" :aspect="view.aspect" sizes="96px" />
                  <img
                    v-else-if="c?.pending?.preview"
                    :src="c.pending.preview"
                    alt=""
                    :style="{ objectPosition: `${c.pending.focusX * 100}% ${c.pending.focusY * 100}%` }"
                  >
                </template>
              </div>
              <v-select
                :model-value="viewValue(view.usage)"
                :items="[{ value: VIEW_DEFAULT, title: 'Titelbild' }, ...viewCandidates]"
                item-title="title"
                item-value="value"
                :label="`${view.label} (${view.format})`"
                density="compact"
                hide-details
                @update:model-value="assignView(view.usage, $event)"
              />
            </div>
          </div>
        </div>
      </v-card-text>
      <v-card-actions class="px-6 pb-4">
        <v-spacer />
        <v-btn @click="closeDialog">{{ mediaChanged ? 'Schließen' : 'Abbrechen' }}</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="uploadLoading"
          :disabled="!form.title || !form.start || !form.end || uploading"
          @click="save"
        >
          Speichern
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>

  <!-- Hidden file input for form dialog -->
  <input
    ref="pendingInputRef"
    type="file"
    accept="image/jpeg,image/png,image/webp,.heic,.heif"
    multiple
    class="d-none"
    @change="onPendingFileSelected"
  >

  <FocalPointDialog
    :model-value="!!focal"
    :src="focal?.src ?? null"
    :focus-x="focal?.focusX"
    :focus-y="focal?.focusY"
    @update:model-value="!$event && (focal = null)"
    @apply="focal?.apply($event.focusX, $event.focusY)"
  />

  <!-- ── Confirmations ──────────────────────────────────────────────────── -->
  <ConfirmDialog
    :model-value="!!deleting"
    title="Termin löschen?"
    :text="`Soll der Termin '${deleting?.title}' wirklich gelöscht werden?`"
    confirm-text="Löschen"
    @update:model-value="!$event && (deleting = null)"
    @confirm="remove"
  />
  <ConfirmDialog
    :model-value="!!deletingMedia"
    title="Datei löschen?"
    :text="`Soll '${deletingMedia?.fileName}' wirklich gelöscht werden?`"
    confirm-text="Löschen"
    @update:model-value="!$event && (deletingMedia = null)"
    @confirm="removeExisting"
  />
</template>

<style scoped>
.media-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 12px;
}
.media-card {
  position: relative;
  display: flex;
  flex-direction: column;
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 8px;
  overflow: hidden;
}
.media-card--pending {
  border-style: dashed;
}
.view-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
}
.view-row__preview {
  flex: none;
  height: 48px;
  border-radius: 6px;
  overflow: hidden;
  background: rgba(var(--v-theme-on-surface), 0.06);
}
.view-row__preview img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.media-card__overlay {
  position: absolute;
  inset: 0;
  z-index: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.45);
}
.media-card--cover,
.media-card--poster {
  border: 2px solid rgb(var(--v-theme-secondary));
}
.media-card__preview {
  aspect-ratio: 1;
  overflow: hidden;
}
.media-card__preview img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.media-card__icon {
  aspect-ratio: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(var(--v-theme-on-surface), 0.05);
}
.media-card__meta {
  padding: 6px 8px 0;
  min-width: 0;
}
.media-card__actions {
  display: flex;
  justify-content: flex-end;
  padding: 0 2px 2px;
}
</style>
