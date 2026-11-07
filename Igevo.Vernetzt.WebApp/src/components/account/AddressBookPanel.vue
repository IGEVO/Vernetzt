<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { api } from '@/services/api'
import { formatDateTime } from '@/services/dates'
import { useUiStore } from '@/stores/ui'
import type { CardDavAccessCreatedDto, CardDavStatusDto } from '@/types/api'

/**
 * Read-only CardDAV address book: create/renew/revoke the personal app password, choose whether
 * organization cards are included, and setup instructions for common clients.
 */
const ui = useUiStore()
const status = useQuery({ queryKey: ['carddav'], queryFn: ({ signal }) => api.get<CardDavStatusDto>('/api/users/me/carddav', signal) })
const busy = ref(false)
const savingSettings = ref(false)
const password = ref<string | null>(null)
const confirmRenew = ref(false)
const confirmRevoke = ref(false)
const copiedKey = ref<string | null>(null)

const host = computed(() => { try { return new URL(status.data.value?.serverUrl ?? '').host } catch { return '' } })
const credentials = computed(() => {
  const data = status.data.value
  if (!data) return []
  const items = [
    { key: 'server', label: 'Server-URL', value: data.serverUrl },
    { key: 'username', label: 'Benutzername', value: data.username }
  ]
  if (password.value) items.push({ key: 'password', label: 'Passwort', value: password.value })
  return items
})

async function createAccess(): Promise<void> {
  confirmRenew.value = false
  busy.value = true
  try {
    const created = await api.post<CardDavAccessCreatedDto>('/api/users/me/carddav/token')
    password.value = created.password
    await status.refetch()
    ui.notify('Der Zugang wurde eingerichtet. Kopiere jetzt das Passwort.', 'success')
  } catch { ui.notify('Der Zugang konnte nicht eingerichtet werden.', 'error') }
  finally { busy.value = false }
}

async function revokeAccess(): Promise<void> {
  confirmRevoke.value = false
  busy.value = true
  try {
    await api.delete('/api/users/me/carddav/token')
    password.value = null
    await status.refetch()
    ui.notify('Der Zugang wurde widerrufen.', 'success')
  } catch { ui.notify('Der Zugang konnte nicht widerrufen werden.', 'error') }
  finally { busy.value = false }
}

async function setIncludeOrganizations(value: boolean | null): Promise<void> {
  savingSettings.value = true
  try {
    await api.put<CardDavStatusDto>('/api/users/me/carddav/settings', { includeOrganizations: !!value })
    await status.refetch()
    ui.notify('Einstellung gespeichert. Deine Geräte übernehmen sie beim nächsten Abgleich.', 'success')
  } catch { ui.notify('Die Einstellung konnte nicht gespeichert werden.', 'error') }
  finally { savingSettings.value = false }
}

async function copy(key: string, value: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value)
    copiedKey.value = key
    setTimeout(() => { if (copiedKey.value === key) copiedKey.value = null }, 2000)
  } catch { ui.notify('Kopieren hat nicht geklappt. Bitte markiere den Text manuell.', 'warning') }
}
</script>

<template>
  <v-card class="mb-6">
    <v-card-item title="Adressbuch abonnieren" subtitle="Kontakte aus Vernetzt auf dem Smartphone und im Mailprogramm (CardDAV)" prepend-icon="mdi-card-account-phone" />
    <v-card-text>
      <p class="mb-4">Hol dir die Kontakte aus Vernetzt in die Kontakte-App deines Smartphones oder Computers. Das Adressbuch aktualisiert sich automatisch und kann nur gelesen werden. Änderungen machst du weiterhin hier in Vernetzt.</p>
      <v-alert type="info" variant="tonal" class="mb-4">
        Das Adressbuch enthält alle Kontakte mit E-Mail-Adresse oder Telefonnummer, die du auch hier sehen kannst. Gib das Passwort deshalb nicht weiter. Wenn ein Gerät verloren geht, widerrufe den Zugang. Das passiert auch automatisch, wenn du dich auf allen Geräten abmeldest.
      </v-alert>

      <v-progress-linear v-if="status.isPending.value" indeterminate class="mb-4" />
      <v-alert v-else-if="status.isError.value" type="warning" variant="tonal">Der Zugang konnte nicht geladen werden.</v-alert>
      <template v-else-if="status.data.value">
        <template v-if="status.data.value.hasAccess">
          <p class="mb-4">
            <v-icon icon="mdi-check-circle" color="success" size="small" class="mr-1" />
            Zugang aktiv seit {{ formatDateTime(status.data.value.createdAt!) }} ·
            {{ status.data.value.lastUsedAt ? `zuletzt abgeglichen ${formatDateTime(status.data.value.lastUsedAt)}` : 'noch nicht genutzt' }}
          </p>

          <v-alert v-if="password" type="success" variant="tonal" class="mb-4" title="Dein Passwort">
            Das Passwort wird nur jetzt angezeigt. Trag es gleich in deinem Gerät ein. Wenn du es verlierst, erzeugst du einfach ein neues.
          </v-alert>

          <div v-for="item in credentials" :key="item.key" class="d-flex align-center ga-2 mb-3">
            <v-text-field :model-value="item.value" :label="item.label" readonly hide-details density="compact" class="flex-grow-1 min-w-0" :class="{ 'font-monospace': item.key === 'password' }" @focus="($event.target as HTMLInputElement).select()" />
            <v-btn :variant="copiedKey === item.key ? 'tonal' : 'flat'" :color="copiedKey === item.key ? 'success' : 'primary'" :prepend-icon="copiedKey === item.key ? 'mdi-check' : 'mdi-content-copy'" min-width="120" :aria-label="`${item.label} kopieren`" @click="copy(item.key, item.value)">{{ copiedKey === item.key ? 'Kopiert' : 'Kopieren' }}</v-btn>
          </div>

          <div class="d-flex flex-wrap ga-2">
            <v-btn variant="outlined" prepend-icon="mdi-key-change" :disabled="busy" @click="confirmRenew = true">Neues Passwort erzeugen</v-btn>
            <v-btn variant="text" color="error" prepend-icon="mdi-link-off" :disabled="busy" @click="confirmRevoke = true">Zugang widerrufen</v-btn>
          </div>
        </template>
        <template v-else>
          <p class="mb-4 text-medium-emphasis">Du hast noch keinen Zugang eingerichtet.</p>
          <v-btn color="primary" size="large" prepend-icon="mdi-plus" :loading="busy" @click="createAccess">Zugang einrichten</v-btn>
        </template>
      </template>
    </v-card-text>
  </v-card>

  <v-card v-if="status.data.value" class="mb-6">
    <v-card-item title="Inhalt des Adressbuchs" prepend-icon="mdi-tune-variant" />
    <v-card-text>
      <v-switch
        :model-value="status.data.value.includeOrganizations"
        :loading="savingSettings"
        :disabled="savingSettings"
        color="primary"
        inset
        hide-details="auto"
        label="Auch Vereine, Abteilungen und Gruppen als eigene Karten"
        hint="Mit Anschrift, E-Mail-Adresse und Website. Fehlende Angaben werden wie auf der öffentlichen Seite vom Verein übernommen."
        persistent-hint
        @update:model-value="setIncludeOrganizations"
      />
    </v-card-text>
  </v-card>

  <v-card>
    <v-card-item title="So richtest du das Adressbuch ein" prepend-icon="mdi-help-circle-outline" />
    <v-card-text>
      <v-expansion-panels variant="accordion">
        <v-expansion-panel title="iPhone und iPad">
          <v-expansion-panel-text>
            <ol class="pl-5">
              <li>Öffne <strong>Einstellungen → Apps → Kontakte → Accounts → Account hinzufügen → Andere</strong>.</li>
              <li>Wähle <strong>CardDAV-Account hinzufügen</strong>.</li>
              <li>Server: <code>{{ host || 'deine Vernetzt-Adresse' }}</code>, dazu Benutzername und Passwort von oben.</li>
            </ol>
          </v-expansion-panel-text>
        </v-expansion-panel>
        <v-expansion-panel title="Mac">
          <v-expansion-panel-text>
            <ol class="pl-5">
              <li>Öffne die App <strong>Kontakte → Einstellungen → Accounts</strong> und klicke auf <strong>+</strong>.</li>
              <li>Wähle <strong>Anderer Kontakte-Account → CardDAV</strong> und als Accounttyp <strong>Manuell</strong>.</li>
              <li>Trag Benutzername, Passwort und als Server <code>{{ host || 'deine Vernetzt-Adresse' }}</code> ein.</li>
            </ol>
          </v-expansion-panel-text>
        </v-expansion-panel>
        <v-expansion-panel title="Android">
          <v-expansion-panel-text>
            <ol class="pl-5">
              <li>Installiere die App <strong>DAVx⁵</strong> (Google Play oder F-Droid).</li>
              <li>Tippe auf <strong>+</strong> und wähle <strong>Mit URL und Benutzername anmelden</strong>.</li>
              <li>Basis-URL ist die Server-URL von oben, dazu Benutzername und Passwort. Aktiviere danach das Adressbuch „Vernetzt“.</li>
            </ol>
          </v-expansion-panel-text>
        </v-expansion-panel>
        <v-expansion-panel title="Thunderbird">
          <v-expansion-panel-text>
            <ol class="pl-5">
              <li>Öffne das <strong>Adressbuch</strong> und wähle <strong>Neues Adressbuch → CardDAV-Adressbuch hinzufügen</strong>.</li>
              <li>Benutzername von oben, als Adresse die Server-URL. Nach der Passwortabfrage wählst du „Vernetzt“ aus.</li>
            </ol>
          </v-expansion-panel-text>
        </v-expansion-panel>
      </v-expansion-panels>
      <p class="mt-4 mb-0 text-body-medium text-medium-emphasis">Wenn du deine E-Mail-Adresse in Vernetzt änderst, passe den Benutzernamen auf deinen Geräten an.</p>
    </v-card-text>
  </v-card>

  <ConfirmDialog v-model="confirmRenew" title="Neues Passwort erzeugen?" text="Das bisherige Passwort funktioniert danach nicht mehr. Du musst das neue auf allen Geräten eintragen." confirm-text="Neues Passwort" confirm-color="primary" @confirm="createAccess" />
  <ConfirmDialog v-model="confirmRevoke" title="Zugang widerrufen?" text="Deine Geräte können das Adressbuch danach nicht mehr abgleichen." confirm-text="Widerrufen" @confirm="revokeAccess" />
</template>
