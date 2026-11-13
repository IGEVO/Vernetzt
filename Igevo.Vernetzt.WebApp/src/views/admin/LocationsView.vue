<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import { api, ApiError } from '@/services/api'
import { useUiStore } from '@/stores/ui'
import type { LocationDto } from '@/types/api'

/** Central list of locations ("Standorte") that clubs, departments and groups can be linked to. */
const ui = useUiStore()
const queryClient = useQueryClient()
const dialog = ref(false)
const busy = ref(false)
const editing = ref<LocationDto | null>(null)
const deleting = ref<LocationDto | null>(null)
const form = reactive({ name: '', description: '', street: '', postalCode: '', city: '' })
const query = useQuery({ queryKey: ['admin', 'locations'], queryFn: ({ signal }) => api.get<LocationDto[]>('/api/locations', signal) })

const deleteText = computed(() => {
  const count = deleting.value?.usageCount ?? 0
  return count > 0
    ? `„${deleting.value?.name}“ wird von ${count} ${count === 1 ? 'Organisation' : 'Organisationen'} genutzt. Beim Löschen werden diese Zuordnungen entfernt. Diese Aktion kann nicht rückgängig gemacht werden.`
    : 'Diese Aktion kann nicht rückgängig gemacht werden.'
})

function address(item: LocationDto): string {
  return [item.street, [item.postalCode, item.city].filter(Boolean).join(' ')].filter(Boolean).join(', ')
}

function open(item?: LocationDto): void {
  editing.value = item ?? null
  Object.assign(form, {
    name: item?.name ?? '',
    description: item?.description ?? '',
    street: item?.street ?? '',
    postalCode: item?.postalCode ?? '',
    city: item?.city ?? ''
  })
  dialog.value = true
}

async function refresh(): Promise<void> {
  await query.refetch()
  await queryClient.invalidateQueries({ queryKey: ['locations', 'active'] })
}

async function save(): Promise<void> {
  if (!form.name.trim()) return
  busy.value = true
  const body = {
    name: form.name.trim(),
    description: form.description.trim() || null,
    street: form.street.trim() || null,
    postalCode: form.postalCode.trim() || null,
    city: form.city.trim() || null
  }
  try {
    if (editing.value) await api.put(`/api/locations/${editing.value.id}`, body)
    else await api.post('/api/locations', body)
    dialog.value = false
    await refresh()
    ui.notify('Standort wurde gespeichert.', 'success')
  } catch (error) {
    ui.notify(error instanceof ApiError && error.status === 400 ? error.message : 'Standort konnte nicht gespeichert werden.', 'error')
  } finally { busy.value = false }
}

async function remove(): Promise<void> {
  if (!deleting.value) return
  try {
    await api.delete(`/api/locations/${deleting.value.id}`)
    deleting.value = null
    await refresh()
    ui.notify('Standort wurde gelöscht.', 'success')
  } catch { ui.notify('Standort konnte nicht gelöscht werden.', 'error') }
}
</script>

<template>
  <PageHeader title="Standorte" subtitle="Hallen, Plätze und andere Orte, die Vereine, Abteilungen und Gruppen nutzen">
    <template #actions><v-btn color="primary" prepend-icon="mdi-plus" @click="open()">Hinzufügen</v-btn></template>
  </PageHeader>
  <v-progress-linear v-if="query.isPending.value" indeterminate />
  <v-alert v-else-if="query.isError.value" type="error">Daten konnten nicht geladen werden.</v-alert>
  <v-alert v-else-if="!query.data.value?.length" type="info" variant="tonal">
    Noch keine Standorte angelegt. Standorte lassen sich Vereinen, Abteilungen und Gruppen zuordnen und beim E-Mail-Versand als Filter nutzen.
  </v-alert>
  <v-table v-else>
    <thead><tr><th>Name</th><th>Adresse</th><th>Beschreibung</th><th class="text-right">Genutzt von</th><th class="text-right">Aktionen</th></tr></thead>
    <tbody>
      <tr v-for="item in query.data.value" :key="item.id">
        <td>{{ item.name }}</td>
        <td>{{ address(item) }}</td>
        <td>{{ item.description }}</td>
        <td class="text-right">{{ item.usageCount }}</td>
        <td class="text-right">
          <v-btn icon="mdi-pencil" variant="text" size="small" :aria-label="`${item.name} bearbeiten`" title="Bearbeiten" @click="open(item)" />
          <v-btn icon="mdi-delete" variant="text" size="small" color="error" :aria-label="`${item.name} löschen`" title="Löschen" @click="deleting = item" />
        </td>
      </tr>
    </tbody>
  </v-table>
  <v-dialog v-model="dialog" max-width="550">
    <v-card :title="editing ? 'Standort bearbeiten' : 'Standort anlegen'">
      <v-form @submit.prevent="save">
        <v-card-text>
          <v-text-field v-model="form.name" label="Name *" maxlength="200" />
          <v-textarea v-model="form.description" label="Beschreibung" rows="2" auto-grow maxlength="1000" />
          <v-text-field v-model="form.street" label="Straße und Hausnummer (optional)" maxlength="200" />
          <v-row density="compact">
            <v-col cols="4"><v-text-field v-model="form.postalCode" label="PLZ" maxlength="20" /></v-col>
            <v-col cols="8"><v-text-field v-model="form.city" label="Ort" maxlength="100" /></v-col>
          </v-row>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn :disabled="busy" @click="dialog = false">Abbrechen</v-btn>
          <v-btn type="submit" color="primary" variant="flat" :loading="busy" :disabled="!form.name.trim()">Speichern</v-btn>
        </v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>
  <ConfirmDialog :model-value="!!deleting" title="Standort löschen?" :text="deleteText" @update:model-value="!$event && (deleting = null)" @confirm="remove" />
</template>
