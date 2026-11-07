<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { objectPosition } from '@/services/eventMedia'

/**
 * Lets the user pick the most important point of an image instead of cropping it.
 * The image is stored uncropped; every view (list icon, detail header, public cards)
 * crops it on the fly with object-position so that the focal point stays visible.
 */
const props = defineProps<{
  modelValue: boolean
  src: string | null
  focusX?: number
  focusY?: number
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  apply: [focus: { focusX: number; focusY: number }]
}>()

const x = ref(0.5)
const y = ref(0.5)
const imageEl = ref<HTMLImageElement>()
let dragging = false

watch(
  () => props.modelValue,
  (open) => {
    if (!open) return
    x.value = props.focusX ?? 0.5
    y.value = props.focusY ?? 0.5
  },
  { immediate: true }
)

const position = computed(() => objectPosition(x.value, y.value))

const previews = [
  { label: 'Liste', aspect: 1, width: 72 },
  { label: 'Karte', aspect: 3 / 2, width: 150 },
  { label: 'Kopfbild', aspect: 2, width: 200 }
]

function setFromPointer(e: PointerEvent): void {
  const rect = imageEl.value?.getBoundingClientRect()
  if (!rect || !rect.width || !rect.height) return
  x.value = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width))
  y.value = Math.min(1, Math.max(0, (e.clientY - rect.top) / rect.height))
}

function onPointerDown(e: PointerEvent): void {
  dragging = true
  ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
  setFromPointer(e)
}

function onPointerMove(e: PointerEvent): void {
  if (dragging) setFromPointer(e)
}

function onPointerUp(): void {
  dragging = false
}

function onKey(e: KeyboardEvent): void {
  const step = e.shiftKey ? 0.1 : 0.02
  const moves: Record<string, [number, number]> = {
    ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step]
  }
  const move = moves[e.key]
  if (!move) return
  e.preventDefault()
  x.value = Math.min(1, Math.max(0, x.value + move[0]))
  y.value = Math.min(1, Math.max(0, y.value + move[1]))
}

function apply(): void {
  emit('apply', { focusX: Math.round(x.value * 1000) / 1000, focusY: Math.round(y.value * 1000) / 1000 })
  emit('update:modelValue', false)
}
</script>

<template>
  <v-dialog :model-value="modelValue" max-width="760" @update:model-value="emit('update:modelValue', $event)">
    <v-card>
      <v-card-title class="pt-4 px-6">Bildausschnitt festlegen</v-card-title>
      <v-card-subtitle class="px-6">Auf den wichtigsten Bildbereich tippen.</v-card-subtitle>
      <v-card-text class="px-6">
        <div class="focal-stage">
          <div
            v-if="src"
            class="focal-frame"
            tabindex="0"
            role="slider"
            aria-label="Fokuspunkt"
            :aria-valuetext="`${Math.round(x * 100)} % horizontal, ${Math.round(y * 100)} % vertikal`"
            @pointerdown="onPointerDown"
            @pointermove="onPointerMove"
            @pointerup="onPointerUp"
            @pointercancel="onPointerUp"
            @keydown="onKey"
          >
            <img ref="imageEl" :src="src" alt="" draggable="false" class="focal-image">
            <div class="focal-marker" :style="{ left: `${x * 100}%`, top: `${y * 100}%` }" />
          </div>
        </div>

        <div class="text-title-small mt-4 mb-2">Vorschau</div>
        <div class="d-flex flex-wrap align-end ga-4">
          <div v-for="p in previews" :key="p.label" class="text-center">
            <div class="focal-preview" :style="{ width: `${p.width}px`, aspectRatio: String(p.aspect) }">
              <img v-if="src" :src="src" alt="" :style="{ objectPosition: position }">
            </div>
            <div class="text-body-small text-medium-emphasis mt-1">{{ p.label }}</div>
          </div>
        </div>
      </v-card-text>
      <v-card-actions class="px-6 pb-4">
        <v-btn variant="text" @click="x = 0.5; y = 0.5">Zurücksetzen</v-btn>
        <v-spacer />
        <v-btn @click="emit('update:modelValue', false)">Abbrechen</v-btn>
        <v-btn color="primary" variant="flat" @click="apply">Übernehmen</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.focal-stage {
  display: flex;
  justify-content: center;
  background: #111;
  border-radius: 8px;
  padding: 8px;
}
.focal-frame {
  position: relative;
  display: inline-block;
  cursor: crosshair;
  touch-action: none;
  user-select: none;
  line-height: 0;
}
.focal-frame:focus-visible {
  outline: 2px solid rgb(var(--v-theme-secondary));
  outline-offset: 2px;
}
.focal-image {
  display: block;
  max-width: 100%;
  max-height: 380px;
}
.focal-marker {
  position: absolute;
  width: 28px;
  height: 28px;
  margin: -14px 0 0 -14px;
  border: 3px solid #fff;
  border-radius: 50%;
  box-shadow: 0 0 0 2px rgba(0, 0, 0, 0.5), inset 0 0 0 2px rgba(0, 0, 0, 0.5);
  pointer-events: none;
}
.focal-preview {
  overflow: hidden;
  border-radius: 6px;
  background: rgba(var(--v-theme-on-surface), 0.08);
}
.focal-preview img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
</style>
