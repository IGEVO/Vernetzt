<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import RichTextEditor from '@/components/RichTextEditor.vue'
import { ApiError, api } from '@/services/api'
import { applyTemplate, portalTemplates, withoutUnchangedTemplate } from '@/services/portalTemplates'
import { useUiStore } from '@/stores/ui'
import type { PortalSettingsDto } from '@/types/api'

type LegalPage = 'imprint' | 'privacy'

const ui = useUiStore()
const query = useQuery({ queryKey: ['admin', 'portal'], queryFn: ({ signal }) => api.get<PortalSettingsDto>('/api/settings/portal', signal) })
const model = reactive<PortalSettingsDto>({
  title: '', subtitle: '', providerName: '', providerUrl: '', noticeText: '', noticeLevel: 'info', hasLogo: false,
  imprint: { mode: 'text', html: '', url: '' },
  privacy: { mode: 'text', html: '', url: '' }
})
const tab = ref<LegalPage>('imprint')
const saving = ref(false)
const logoBusy = ref(false)
const logoVersion = ref(Date.now())
const logoInput = ref<HTMLInputElement>()
const legalPages: { key: LegalPage; label: string }[] = [{ key: 'imprint', label: 'Impressum' }, { key: 'privacy', label: 'Datenschutz' }]

watch(() => query.data.value, (data) => {
  if (!data) return
  // Copy explicitly: query data is a reactive proxy, which structuredClone() cannot clone.
  Object.assign(model, { ...data, imprint: { ...data.imprint }, privacy: { ...data.privacy } })
  applyTemplate('imprint', model.imprint)
  applyTemplate('privacy', model.privacy)
}, { immediate: true })

const isTemplate = (page: LegalPage) => model[page].html === portalTemplates[page]

async function save(): Promise<void> {
  saving.value = true
  try {
    await api.put('/api/settings/portal', {
      ...model,
      imprint: withoutUnchangedTemplate('imprint', model.imprint),
      privacy: withoutUnchangedTemplate('privacy', model.privacy)
    })
    await query.refetch()
    ui.notify('Gespeichert. Änderungen erscheinen auf der öffentlichen Seite innerhalb einer Minute.', 'success')
  } catch (error) {
    ui.notify(error instanceof ApiError ? error.message : 'Einstellungen konnten nicht gespeichert werden.', 'error')
  } finally { saving.value = false }
}

async function uploadLogo(event: Event): Promise<void> {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  target.value = ''
  if (!file) return
  if (file.size > 10 * 1024 * 1024 || !['image/jpeg', 'image/png', 'image/webp', 'image/heic', 'image/heif'].includes(file.type)) {
    ui.notify('Bitte JPG, PNG, WebP oder HEIC bis maximal 10 MB auswählen.', 'warning'); return
  }
  logoBusy.value = true
  try {
    const form = new FormData()
    form.append('file', file)
    await api.postForm('/api/settings/portal/logo', form)
    model.hasLogo = true
    logoVersion.value = Date.now()
    ui.notify('Logo wurde hochgeladen.', 'success')
  } catch (error) {
    ui.notify(error instanceof ApiError ? error.message : 'Logo konnte nicht hochgeladen werden.', 'error')
  } finally { logoBusy.value = false }
}

async function removeLogo(): Promise<void> {
  logoBusy.value = true
  try { await api.delete('/api/settings/portal/logo'); model.hasLogo = false; ui.notify('Logo wurde entfernt.', 'success') }
  catch { ui.notify('Logo konnte nicht entfernt werden.', 'error') }
  finally { logoBusy.value = false }
}
</script>

<template>
  <v-card>
    <v-card-item title="Öffentliches Portal" subtitle="Texte und Logo der öffentlichen Vereinsübersicht" prepend-icon="mdi-web" />
    <v-progress-linear v-if="query.isPending.value" indeterminate />
    <v-card-text v-else-if="query.isError.value">
      <v-alert type="error" variant="tonal">Portal-Einstellungen konnten nicht geladen werden.</v-alert>
    </v-card-text>
    <v-form v-else @submit.prevent="save">
      <v-card-text>
        <v-row>
          <v-col cols="12" md="7">
            <v-text-field v-model="model.title" label="Titel der Startseite" placeholder="Vereine und Termine" counter="120" maxlength="120" persistent-placeholder />
            <v-textarea v-model="model.subtitle" label="Untertitel" placeholder="Kontakte, Abteilungen und öffentliche Termine, gepflegt von den Vereinen selbst." counter="300" maxlength="300" rows="2" auto-grow persistent-placeholder />
            <v-row density="compact">
              <v-col cols="12" sm="6"><v-text-field v-model="model.providerName" label="Anbieter (optional)" placeholder="z. B. Stadt Musterstadt" hint="Erscheint im Footer als „Ein Angebot von …“" persistent-hint /></v-col>
              <v-col cols="12" sm="6"><v-text-field v-model="model.providerUrl" label="Website des Anbieters (optional)" placeholder="https://" :disabled="!model.providerName.trim()" /></v-col>
            </v-row>
          </v-col>
          <v-col cols="12" md="5">
            <div class="text-label-large text-medium-emphasis mb-2">Logo auf der Startseite</div>
            <div class="portal-logo-preview mb-3">
              <img v-if="model.hasLogo" :src="`/api/settings/portal/logo?t=${logoVersion}`" alt="Aktuelles Logo">
              <span v-else class="text-body-medium text-medium-emphasis">Kein Logo hinterlegt</span>
            </div>
            <input ref="logoInput" type="file" accept="image/jpeg,image/png,image/webp,image/heic,image/heif" hidden @change="uploadLogo">
            <v-btn variant="tonal" prepend-icon="mdi-upload" :loading="logoBusy" @click="logoInput?.click()">{{ model.hasLogo ? 'Ersetzen' : 'Hochladen' }}</v-btn>
            <v-btn v-if="model.hasLogo" variant="text" color="error" class="ml-2" :disabled="logoBusy" @click="removeLogo">Entfernen</v-btn>
            <p class="text-body-small text-medium-emphasis mt-2 mb-0">Quadratisch oder Hochformat empfohlen, PNG oder WebP mit transparentem Hintergrund. Das Logo steht links neben dem Titel auf dem blauen Hintergrund der Startseite (Vorschau oben).</p>
          </v-col>
        </v-row>

        <div class="text-label-large text-medium-emphasis mt-4 mb-2">Hinweisbanner (optional)</div>
        <v-row density="compact">
          <v-col cols="12" md="8">
            <v-textarea v-model="model.noticeText" label="Text des Banners" placeholder="z. B. Der Datenbestand befindet sich noch im Aufbau." counter="500" maxlength="500" rows="2" auto-grow hint="Erscheint auf allen Seiten unter der Kopfzeile. Leer lassen, um keinen Banner anzuzeigen." persistent-hint />
          </v-col>
          <v-col cols="12" md="4">
            <v-btn-toggle v-model="model.noticeLevel" mandatory density="comfortable" variant="outlined" :color="model.noticeLevel === 'warning' ? 'warning' : 'primary'" :disabled="!model.noticeText.trim()">
              <v-btn value="info" prepend-icon="mdi-information-outline">Hinweis</v-btn>
              <v-btn value="warning" prepend-icon="mdi-alert-outline">Warnung</v-btn>
            </v-btn-toggle>
          </v-col>
        </v-row>

        <v-tabs v-model="tab" class="mt-4" color="primary">
          <v-tab v-for="page in legalPages" :key="page.key" :value="page.key">{{ page.label }}</v-tab>
        </v-tabs>
        <v-divider class="mb-4" />
        <v-window v-model="tab">
          <v-window-item v-for="page in legalPages" :key="page.key" :value="page.key">
            <v-btn-toggle v-model="model[page.key].mode" mandatory density="comfortable" variant="outlined" color="primary" class="mb-4">
              <v-btn value="text" prepend-icon="mdi-text-box-edit-outline">Eigener Text</v-btn>
              <v-btn value="link" prepend-icon="mdi-open-in-new">Externer Link</v-btn>
            </v-btn-toggle>
            <template v-if="model[page.key].mode === 'text'">
              <v-alert v-if="isTemplate(page.key)" type="info" variant="tonal" density="compact" class="mb-3">
                Vorlage – bitte die Angaben in eckigen Klammern ersetzen. Eine unveränderte Vorlage wird nicht veröffentlicht.
              </v-alert>
              <RichTextEditor v-model="model[page.key].html" headings />
            </template>
            <v-text-field v-else v-model="model[page.key].url" :label="`Adresse der Seite (${page.label})`" placeholder="https://" hint="Der Link im Footer führt direkt auf diese Seite." persistent-hint />
          </v-window-item>
        </v-window>
      </v-card-text>
      <v-card-actions><v-spacer /><v-btn type="submit" color="primary" variant="flat" :loading="saving">Speichern</v-btn></v-card-actions>
    </v-form>
  </v-card>
</template>

<style scoped>
.portal-logo-preview {
  display: flex; align-items: center; justify-content: center;
  min-height: 136px; padding: 16px; border-radius: 8px; background: #0d4a70; /* brand colour of the public portal */
}
.portal-logo-preview img { max-width: 140px; max-height: 104px; }
.portal-logo-preview .text-medium-emphasis { color: #c3d6e3 !important; }
</style>
