<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { apiRequest } from '@/services/api'
import { bumpAvatarVersion } from '@/services/avatars'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import AvatarCropDialog from '@/components/AvatarCropDialog.vue'
import AvatarImage from '@/components/AvatarImage.vue'

const props = withDefaults(defineProps<{ type: 'users' | 'clubs' | 'departments'; id?: string; hasAvatar: boolean; initials: string; editable?: boolean; size?: number }>(), { id: undefined, editable: false, size: 120 })
const emit = defineEmits<{ changed: [value: boolean] }>()
const ui = useUiStore()
const auth = useAuthStore()
const client = useQueryClient()
const input = ref<HTMLInputElement>()
const busy = ref(false)
const cropDialog = ref(false)
const selectedFile = ref<File | null>(null)
// Local copy: query results are not deeply reactive, so the parent cannot reliably patch `hasAvatar` in place.
const current = ref(props.hasAvatar)
watch(() => props.hasAvatar, (value) => { current.value = value })
const endpoint = computed(() => `/api/avatars/${props.type}/${props.type === 'users' && !props.id ? 'me' : props.id}`)

function upload(event: Event): void {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  target.value = ''
  if (!file) return
  if (file.size > 5 * 1024 * 1024 || !['image/jpeg', 'image/png', 'image/webp', 'image/heic', 'image/heif'].includes(file.type)) {
    ui.notify('Bitte JPG, PNG, WebP oder HEIC bis maximal 5 MB auswählen.', 'warning'); return
  }
  selectedFile.value = file
  cropDialog.value = true
}
async function changed(value: boolean): Promise<void> {
  current.value = value
  bumpAvatarVersion()
  emit('changed', value)
  // Refresh every view that shows this avatar (lists, detail pages, app bar).
  const keys = props.type === 'users' ? [['user', 'me'], ['contacts'], ['admin', 'users']] : props.type === 'clubs' ? [['club', props.id], ['clubs'], ['dashboard']] : [['department', props.id], ['club'], ['dashboard']]
  await Promise.all(keys.map((queryKey) => client.invalidateQueries({ queryKey })))
  if (props.type === 'users') await auth.initialize(true)
}
async function uploadCropped(file: File): Promise<void> {
  busy.value = true
  try {
    const form = new FormData(); form.append('file', file, file.name)
    await apiRequest(endpoint.value, { method: 'POST', body: form })
    await changed(true)
    ui.notify('Bild wurde aktualisiert.', 'success')
  } catch { ui.notify('Das Bild konnte nicht hochgeladen werden.', 'error') }
  finally { busy.value = false }
}
async function remove(): Promise<void> {
  busy.value = true
  try { await apiRequest(endpoint.value, { method: 'DELETE' }); await changed(false); ui.notify('Bild wurde entfernt.', 'success') }
  catch { ui.notify('Das Bild konnte nicht entfernt werden.', 'error') }
  finally { busy.value = false }
}
</script>
<template>
  <div class="text-center">
    <AvatarImage :type="type" :id="id" :has-avatar="current" :initials="initials" :size="size" />
    <div v-if="editable" class="mt-3 d-flex flex-wrap ga-2 justify-center">
      <input ref="input" type="file" hidden accept=".jpg,.jpeg,.png,.webp,.heic,.heif" @change="upload">
      <v-btn size="small" variant="tonal" prepend-icon="mdi-camera" :loading="busy" @click="input?.click()">{{ current ? 'Ändern' : 'Bild hochladen' }}</v-btn>
      <v-btn v-if="current" size="small" variant="text" color="error" prepend-icon="mdi-delete" :disabled="busy" @click="remove">Entfernen</v-btn>
    </div>
    <AvatarCropDialog v-model="cropDialog" :file="selectedFile" @cropped="uploadCropped" />
  </div>
</template>
