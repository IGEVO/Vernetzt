<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '@/services/api'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'

const auth = useAuthStore(); const ui = useUiStore(); const router = useRouter()
const exporting = ref(false); const showDelete = ref(false); const deleting = ref(false); const confirmEmail = ref(''); const reason = ref('')

async function downloadExport(): Promise<void> {
  exporting.value = true
  try {
    const response = await fetch('/api/gdpr/export/download', { credentials: 'same-origin' })
    if (!response.ok) throw new Error()
    const url = URL.createObjectURL(await response.blob())
    const anchor = document.createElement('a'); anchor.href = url; anchor.download = `datenexport_${new Date().toISOString().slice(0, 10)}.zip`; anchor.click()
    URL.revokeObjectURL(url)
  } catch { ui.notify('Datenexport konnte nicht erstellt werden.', 'error') }
  finally { exporting.value = false }
}
async function deleteAccount(): Promise<void> {
  deleting.value = true
  try { await api.post('/api/gdpr/delete-account', { confirmEmail: confirmEmail.value, reason: reason.value || null }); auth.clear(); await router.replace('/login') }
  catch { ui.notify('Konto konnte nicht gelöscht werden.', 'error') }
  finally { deleting.value = false }
}
</script>

<template>
  <v-card class="mb-6">
    <v-card-item title="Datenauskunft" subtitle="DSGVO Art. 15 / Art. 20" prepend-icon="mdi-download" />
    <v-card-text>
      <p class="mb-4">Lade eine Kopie deiner gespeicherten Daten als JSON-Datei herunter.</p>
      <v-btn variant="outlined" prepend-icon="mdi-download" :loading="exporting" @click="downloadExport">Datenexport herunterladen</v-btn>
    </v-card-text>
  </v-card>
  <v-card>
    <v-card-item title="Konto löschen" subtitle="DSGVO Art. 17" prepend-icon="mdi-account-remove" />
    <v-card-text>
      <v-alert type="warning" variant="tonal" class="mb-4">Dein Konto und deine Zuordnungen werden dauerhaft gelöscht. Diese Aktion kann nicht rückgängig gemacht werden.</v-alert>
      <v-btn color="error" variant="outlined" prepend-icon="mdi-delete" @click="showDelete = true">Konto löschen</v-btn>
    </v-card-text>
  </v-card>
  <v-dialog v-model="showDelete" max-width="550">
    <v-card title="Konto endgültig löschen?">
      <v-form @submit.prevent="deleteAccount">
        <v-card-text>
          <p class="mb-4">Gib zur Bestätigung deine E-Mail-Adresse <strong>{{ auth.user?.email }}</strong> ein.</p>
          <v-text-field v-model="confirmEmail" label="E-Mail-Adresse bestätigen" type="email" autocomplete="off" />
          <v-textarea v-model="reason" label="Grund (optional)" rows="2" />
        </v-card-text>
        <v-card-actions><v-spacer /><v-btn @click="showDelete = false">Abbrechen</v-btn><v-btn type="submit" color="error" variant="flat" :loading="deleting" :disabled="confirmEmail.trim().toLowerCase() !== auth.user?.email.toLowerCase()">Endgültig löschen</v-btn></v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>
</template>
