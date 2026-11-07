<script setup lang="ts">
import type { IconName } from './AppIcon.vue'

type Preference = 'System' | 'Light' | 'Dark'
const order: Preference[] = ['System', 'Light', 'Dark']
const labels: Record<Preference, string> = { System: 'wie Gerät', Light: 'hell', Dark: 'dunkel' }
const icons: Record<Preference, IconName> = { System: 'auto', Light: 'sun', Dark: 'moon' }

// The server cannot know the stored choice; the inline head script already applied it before
// paint, so the button only syncs its own label after hydration.
const preference = ref<Preference>('System')

onMounted(() => {
  try {
    const stored = localStorage.getItem('themePreference')
    if (stored === 'Light' || stored === 'Dark') preference.value = stored
  } catch { /* storage blocked: stay with system setting */ }
})

function cycle() {
  const next = order[(order.indexOf(preference.value) + 1) % order.length]!
  preference.value = next
  const root = document.documentElement
  if (next === 'System') delete root.dataset.theme
  else root.dataset.theme = next.toLowerCase()
  try {
    if (next === 'System') localStorage.removeItem('themePreference')
    else localStorage.setItem('themePreference', next)
  } catch { /* storage blocked: the choice lasts for this page only */ }
}
</script>

<template>
  <button
    type="button" class="icon-button" :title="`Farbschema: ${labels[preference]}`"
    :aria-label="`Farbschema: ${labels[preference]}. Zum Wechseln aktivieren.`" @click="cycle"
  >
    <AppIcon :name="icons[preference]" />
  </button>
</template>
