<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import type Cropper from 'cropperjs'

const props = defineProps<{ modelValue: boolean; file: File | null }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean]; cropped: [file: File] }>()
const host = ref<HTMLElement>()
const busy = ref(false)
const error = ref('')
let cropper: Cropper | null = null
let objectUrl: string | null = null

function dispose(): void {
  cropper?.destroy()
  cropper = null
  if (objectUrl) URL.revokeObjectURL(objectUrl)
  objectUrl = null
  if (host.value) host.value.replaceChildren()
}

async function initialize(): Promise<void> {
  dispose()
  error.value = ''
  if (!props.modelValue || !props.file) return
  await nextTick()
  if (!host.value) return
  try {
    const { default: CropperClass } = await import('cropperjs')
    const image = document.createElement('img')
    objectUrl = URL.createObjectURL(props.file)
    image.src = objectUrl
    image.alt = 'Vorschau des ausgewählten Bilds'
    await image.decode()
    cropper = new CropperClass(image, {
      container: host.value,
    template: '<cropper-canvas background><cropper-image rotatable scalable translatable></cropper-image><cropper-shade hidden></cropper-shade><cropper-selection aspect-ratio="1" initial-coverage="0.8" movable resizable><cropper-grid role="grid" bordered covered></cropper-grid><cropper-crosshair centered></cropper-crosshair><cropper-handle action="move" theme-color="rgba(255,255,255,.35)"></cropper-handle><cropper-handle action="n-resize"></cropper-handle><cropper-handle action="e-resize"></cropper-handle><cropper-handle action="s-resize"></cropper-handle><cropper-handle action="w-resize"></cropper-handle><cropper-handle action="ne-resize"></cropper-handle><cropper-handle action="nw-resize"></cropper-handle><cropper-handle action="se-resize"></cropper-handle><cropper-handle action="sw-resize"></cropper-handle></cropper-selection></cropper-canvas>'
    })
    await fitSelectionToImage(cropper)
  } catch { error.value = 'Dieses Bild kann der Browser nicht als Vorschau öffnen. Bitte verwende JPG, PNG oder WebP.' }
}

/** Image position inside the cropper canvas (canvas-relative pixels). */
function imageRect(instance: Cropper): { x: number; y: number; width: number; height: number } | null {
  const canvas = instance.getCropperCanvas()
  const img = instance.getCropperImage()
  if (!canvas || !img) return null
  const c = canvas.getBoundingClientRect()
  const i = img.getBoundingClientRect()
  // The dialog may still be scaled by its opening transition: convert to unscaled canvas pixels.
  const scale = canvas.offsetWidth ? c.width / canvas.offsetWidth : 1
  if (!scale) return null
  return { x: (i.left - c.left) / scale, y: (i.top - c.top) / scale, width: i.width / scale, height: i.height / scale }
}

/**
 * Default selection: the largest centered square inside the image (instead of a fixed share
 * of the canvas, which may reach beyond the image). Afterwards the selection stays inside the image.
 */
async function fitSelectionToImage(instance: Cropper): Promise<void> {
  const img = instance.getCropperImage()
  const selection = instance.getCropperSelection()
  if (!img || !selection) return
  await img.$ready()
  await new Promise(requestAnimationFrame)
  const rect = imageRect(instance)
  if (!rect || !rect.width || !rect.height) return
  const size = Math.min(rect.width, rect.height)
  selection.$change(rect.x + (rect.width - size) / 2, rect.y + (rect.height - size) / 2, size, size)

  selection.addEventListener('change', (event) => {
    const r = imageRect(instance)
    const d = (event as CustomEvent<{ x: number; y: number; width: number; height: number }>).detail
    if (!r || !d) return
    const eps = 0.5
    if (d.x < r.x - eps || d.y < r.y - eps || d.x + d.width > r.x + r.width + eps || d.y + d.height > r.y + r.height + eps) {
      event.preventDefault()
    }
  })
}

async function apply(): Promise<void> {
  const selection = cropper?.getCropperSelection()
  if (!selection) return
  busy.value = true
  try {
    const canvas = await selection.$toCanvas({ width: 512, height: 512 })
    const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob((value) => value ? resolve(value) : reject(new Error('Crop failed')), 'image/webp', 0.9))
    emit('cropped', new File([blob], 'avatar.webp', { type: 'image/webp' }))
    emit('update:modelValue', false)
  } finally { busy.value = false }
}

watch(() => [props.modelValue, props.file] as const, initialize, { flush: 'post' })
watch(() => props.modelValue, (open) => { if (!open) dispose() })
onBeforeUnmount(dispose)
</script>

<template>
  <v-dialog :model-value="modelValue" max-width="720" persistent @update:model-value="emit('update:modelValue', $event)">
    <v-card>
      <v-card-title>Bildausschnitt wählen</v-card-title>
      <v-card-text><v-alert v-if="error" type="error" class="mb-3">{{ error }}</v-alert><div ref="host" class="crop-host" aria-label="Quadratischen Bildausschnitt wählen" /></v-card-text>
      <v-card-actions><v-spacer /><v-btn :disabled="busy" @click="emit('update:modelValue', false)">Abbrechen</v-btn><v-btn color="primary" variant="flat" :loading="busy" :disabled="!!error" @click="apply">Übernehmen</v-btn></v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.crop-host { height: min(65vh, 520px); overflow: hidden; }
.crop-host :deep(cropper-canvas) { height: 100%; width: 100%; }
</style>
