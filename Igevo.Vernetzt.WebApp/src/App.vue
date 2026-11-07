<script setup lang="ts">
import { onMounted } from 'vue'
import { RouterView } from 'vue-router'
import { useRegisterSW } from 'virtual:pwa-register/vue'
import { useUiStore } from '@/stores/ui'

const ui = useUiStore()
const { needRefresh, updateServiceWorker } = useRegisterSW({ immediate: true })
onMounted(() => { ui.updateAvailable = needRefresh.value })
</script>

<template>
  <v-app>
    <RouterView />
    <v-snackbar v-model="ui.snackbar.open" :color="ui.snackbar.color" location="bottom end" timeout="5000">
      {{ ui.snackbar.text }}
      <template #actions><v-btn variant="text" @click="ui.snackbar.open = false">Schließen</v-btn></template>
    </v-snackbar>
    <v-snackbar :model-value="needRefresh" color="info" location="top" :timeout="-1">
      Eine neue Version ist verfügbar.
      <template #actions><v-btn variant="text" @click="updateServiceWorker(true)">Jetzt aktualisieren</v-btn></template>
    </v-snackbar>
  </v-app>
</template>
