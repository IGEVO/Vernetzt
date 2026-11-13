<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterView } from 'vue-router'
import { useRegisterSW } from 'virtual:pwa-register/vue'
import { useUiStore } from '@/stores/ui'

const ui = useUiStore()
// Set when the waiting worker belongs to the build this page already runs.
let pageIsCurrent = false
const { needRefresh, updateServiceWorker } = useRegisterSW({
  immediate: true,
  // The plugin reloads whenever the new worker takes control; skip that if nothing changes for this page.
  onNeedReload: () => { if (!pageIsCurrent) window.location.reload() }
})
const showUpdatePrompt = ref(false)

// Navigations are network-only, so a freshly loaded page already runs the new build while
// the matching service worker is still waiting. Only prompt if the running code is outdated.
function waitingWorkerHasAsset(worker: ServiceWorker, url: string): Promise<boolean> {
  return new Promise((resolve) => {
    const channel = new MessageChannel()
    const timer = setTimeout(() => resolve(false), 2000)
    channel.port1.onmessage = (event) => { clearTimeout(timer); resolve(event.data === true) }
    worker.postMessage({ type: 'HAS_ASSET', url }, [channel.port2])
  })
}

watch(needRefresh, async (value) => {
  if (!value) { showUpdatePrompt.value = false; ui.updateAvailable = false; return }
  const waiting = (await navigator.serviceWorker.getRegistration())?.waiting
  if (waiting && await waitingWorkerHasAsset(waiting, import.meta.url)) {
    // Page is already up to date: activate the new worker silently, no reload needed.
    pageIsCurrent = true
    await updateServiceWorker(false)
    needRefresh.value = false
    return
  }
  showUpdatePrompt.value = true
  ui.updateAvailable = true
}, { immediate: true })
</script>

<template>
  <v-app>
    <RouterView />
    <v-snackbar v-model="ui.snackbar.open" :color="ui.snackbar.color" location="bottom end" timeout="5000">
      {{ ui.snackbar.text }}
      <template #actions><v-btn variant="text" @click="ui.snackbar.open = false">Schließen</v-btn></template>
    </v-snackbar>
    <v-snackbar :model-value="showUpdatePrompt" color="info" location="top" :timeout="-1">
      Eine neue Version ist verfügbar.
      <template #actions><v-btn variant="text" @click="updateServiceWorker(true)">Jetzt aktualisieren</v-btn></template>
    </v-snackbar>
  </v-app>
</template>
