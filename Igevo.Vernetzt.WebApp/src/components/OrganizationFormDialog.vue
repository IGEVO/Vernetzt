<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { api, ApiError } from '@/services/api'
import { useUiStore } from '@/stores/ui'
import { visibilityOptions } from '@/services/visibility'
import SocialMediaLinksEditor from '@/components/SocialMediaLinksEditor.vue'
import type { ClubDto, DepartmentDto, NamedEntityDto, SocialMediaLinkRow, VisibilityLevel } from '@/types/api'

/**
 * Create / edit dialog shared by clubs, departments and groups, so all forms look and behave the same.
 * A group is a department below a department: it has no own address; pass `parentDepartmentId` to create one.
 */
const props = defineProps<{
  kind: 'club' | 'department' | 'group'
  organization?: ClubDto | DepartmentDto | null
  clubId?: string
  parentDepartmentId?: string
}>()
const open = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [value: ClubDto | DepartmentDto] }>()
const ui = useUiStore()
const busy = ref(false)
const formRef = ref<{ validate: () => Promise<{ valid: boolean }> }>()
const form = reactive({ name: '', shortName: '', description: '', clubTypeId: null as string | null, street: '', postalCode: '', city: '', email: '', website: '', contactPersonsVisibility: 1 as VisibilityLevel })
const socialMediaLinks = ref<SocialMediaLinkRow[]>([])
const isClub = computed(() => props.kind === 'club')
const isGroup = computed(() => props.kind === 'group' || (!!props.organization && 'isGroup' in props.organization && props.organization.isGroup))
const editing = computed(() => !!props.organization)
const noun = computed(() => isClub.value ? 'Verein' : isGroup.value ? 'Gruppe' : 'Abteilung')
const descriptionHint = computed(() => isGroup.value
  ? 'z. B. Altersklasse, Trainingszeiten und -ort'
  : 'z. B. Angebot, Trainingszeiten oder Hinweise für Interessierte')
/** Empty contact blocks are filled from the higher levels on the public portal. */
const inheritanceHint = computed(() => isClub.value ? null : isGroup.value
  ? 'Leere Angaben (Adresse, E-Mail, Webseite, Social Media) zeigt das öffentliche Portal mit den Daten der Abteilung bzw. des Vereins an und kennzeichnet sie als übernommen.'
  : 'Leere Angaben (Adresse, E-Mail, Webseite, Social Media) zeigt das öffentliche Portal mit den Daten des Vereins an und kennzeichnet sie als übernommen.')
const title = computed(() => `${noun.value} ${editing.value ? 'bearbeiten' : 'anlegen'}`)
const clubTypes = useQuery({ queryKey: ['club-types', 'active'], queryFn: ({ signal }) => api.get<NamedEntityDto[]>('/api/clubtypes/active', signal), enabled: computed(() => open.value && isClub.value) })

const rules = {
  required: (value: string) => !!value?.trim() || 'Pflichtfeld',
  email: (value: string) => !value || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value) || 'Bitte eine gültige E-Mail-Adresse eingeben',
  url: (value: string) => !value || /^https?:\/\/\S+\.\S+/.test(value) || 'Bitte mit http:// oder https:// angeben'
}

watch(open, (value) => {
  if (!value) return
  const item = props.organization
  Object.assign(form, {
    name: item?.name ?? '',
    shortName: (item && 'shortName' in item ? item.shortName : '') ?? '',
    description: item?.description ?? '',
    clubTypeId: item && 'clubTypeId' in item ? item.clubTypeId ?? null : null,
    street: item?.street ?? '',
    postalCode: item?.postalCode ?? '',
    city: item?.city ?? '',
    email: item?.email ?? '',
    website: (item && 'websiteUrl' in item ? item.websiteUrl : item && 'website' in item ? item.website : '') ?? '',
    contactPersonsVisibility: item?.contactPersonsVisibility ?? 1
  })
  socialMediaLinks.value = (item?.socialMediaLinks ?? []).map((link, index) => ({ key: index, platform: link.platform, url: link.url }))
}, { immediate: true })

async function submit(): Promise<void> {
  if (!(await formRef.value?.validate())?.valid) return
  busy.value = true
  // Groups have no own address; existing values are kept as they are.
  const address = isGroup.value
    ? { street: props.organization?.street ?? null, postalCode: props.organization?.postalCode ?? null, city: props.organization?.city ?? null }
    : { street: form.street || null, postalCode: form.postalCode || null, city: form.city || null }
  const common = { name: form.name.trim(), shortName: form.shortName.trim() || null, description: form.description.trim() || null, ...address, email: form.email || null, contactPersonsVisibility: form.contactPersonsVisibility,
    socialMediaLinks: socialMediaLinks.value
      .filter(row => row.url.trim() && row.platform !== null)
      .map(row => ({ platform: row.platform!, url: row.url.trim() })) }
  try {
    let result: ClubDto | DepartmentDto
    if (isClub.value) {
      const body = { ...common, clubTypeId: form.clubTypeId, websiteUrl: form.website || null }
      result = editing.value ? await api.put<ClubDto>(`/api/clubs/${props.organization!.id}`, body) : await api.post<ClubDto>('/api/clubs', body)
    } else {
      const body = { ...common, website: form.website || null }
      result = editing.value ? await api.put<DepartmentDto>(`/api/departments/${props.organization!.id}`, body) : await api.post<DepartmentDto>('/api/departments', { ...body, clubId: props.clubId, parentDepartmentId: props.parentDepartmentId ?? null })
    }
    open.value = false
    emit('saved', result)
    ui.notify(`${noun.value} wurde ${editing.value ? 'gespeichert' : 'angelegt'}.`, 'success')
  } catch (error) {
    ui.notify(error instanceof ApiError && error.errors.length ? error.errors.join(' ') : `${noun.value} konnte nicht ${editing.value ? 'gespeichert' : 'angelegt'} werden.`, 'error')
  } finally { busy.value = false }
}
</script>

<template>
  <v-dialog v-model="open" max-width="650" scrollable>
    <v-card :title="title">
      <v-form ref="formRef" @submit.prevent="submit">
        <v-card-text>
          <v-alert type="info" variant="tonal" density="compact" class="mb-5">
            Alle Angaben hier sind <strong>öffentlich sichtbar</strong>, auch im öffentlichen Portal.
            Ausnahme: Für Kontaktpersonen legst du die Sichtbarkeit unten selbst fest.
          </v-alert>
          <v-text-field v-model="form.name" label="Name *" :rules="[rules.required]" maxlength="200" />
          <v-text-field v-model="form.shortName" label="Abkürzung (optional)" hint="z. B. „TVM“ für „Turnverein Musterstadt“. Wird u. a. im Adressbuch hinter Positionen verwendet." persistent-hint maxlength="20" counter="20" class="mb-2" />
          <v-select v-if="isClub" v-model="form.clubTypeId" label="Vereinstyp" :items="clubTypes.data.value ?? []" item-title="name" item-value="id" clearable :loading="clubTypes.isFetching.value" />
          <v-textarea v-model="form.description" label="Infos" :hint="descriptionHint" persistent-hint rows="3" auto-grow counter="4000" maxlength="4000" class="mb-4" />
          <p v-if="inheritanceHint" class="text-body-2 text-medium-emphasis mb-3" data-testid="inheritance-hint">{{ inheritanceHint }}</p>
          <template v-if="!isGroup">
            <v-text-field v-model="form.street" label="Straße und Hausnummer" />
            <v-row dense>
              <v-col cols="4"><v-text-field v-model="form.postalCode" label="PLZ" maxlength="10" /></v-col>
              <v-col cols="8"><v-text-field v-model="form.city" label="Ort" /></v-col>
            </v-row>
          </template>
          <v-text-field v-model="form.email" label="E-Mail" type="email" :rules="[rules.email]" />
          <v-text-field v-model="form.website" label="Webseite" placeholder="https://" :rules="[rules.url]" />
          <SocialMediaLinksEditor v-model="socialMediaLinks" />
          <v-divider class="mt-2 mb-5" />
          <div class="text-subtitle-2 mb-1">Kontaktpersonen</div>
          <p class="text-body-2 text-medium-emphasis mb-3">Lege fest, wer die Kontaktpersonen {{ isClub ? 'dieses Vereins' : isGroup ? 'dieser Gruppe' : 'dieser Abteilung' }} sehen darf.</p>
          <v-select v-model="form.contactPersonsVisibility" label="Kontaktpersonen sichtbar für" :items="visibilityOptions" hide-details />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn :disabled="busy" @click="open = false">Abbrechen</v-btn>
          <v-btn type="submit" color="primary" variant="flat" :loading="busy">{{ editing ? 'Speichern' : 'Anlegen' }}</v-btn>
        </v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>
</template>
