<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import { api } from '@/services/api'
import { useUiStore } from '@/stores/ui'
import type { NamedEntityDto } from '@/types/api'
const props = defineProps<{ kind: 'club-types' | 'contact-positions' }>(); const ui = useUiStore(); const dialog = ref(false); const deleting = ref<NamedEntityDto | null>(null); const editing = ref<NamedEntityDto | null>(null); const form = reactive({ name: '', description: '', isActive: true })
const endpoint = computed(() => props.kind === 'club-types' ? '/api/clubtypes' : '/api/contactpositions'); const title = computed(() => props.kind === 'club-types' ? 'Vereinstypen' : 'Kontaktpositionen')
const query = useQuery({ queryKey: computed(() => ['admin', props.kind]), queryFn: ({ signal }) => api.get<NamedEntityDto[]>(endpoint.value, signal) })
function open(item?: NamedEntityDto): void { editing.value = item ?? null; Object.assign(form, item ? { name: item.name, description: item.description ?? '', isActive: item.isActive ?? true } : { name: '', description: '', isActive: true }); dialog.value = true }
async function save(): Promise<void> { try { if (editing.value) await api.put(`${endpoint.value}/${editing.value.id}`, form); else await api.post(endpoint.value, form); dialog.value = false; await query.refetch(); ui.notify('Eintrag wurde gespeichert.', 'success') } catch { ui.notify('Eintrag konnte nicht gespeichert werden.', 'error') } }
async function remove(): Promise<void> { if (!deleting.value) return; try { await api.delete(`${endpoint.value}/${deleting.value.id}`); deleting.value = null; await query.refetch(); ui.notify('Eintrag wurde gelöscht.', 'success') } catch { ui.notify('Eintrag konnte nicht gelöscht werden.', 'error') } }
</script>
<template>
  <PageHeader :title="title"><template #actions><v-btn color="primary" prepend-icon="mdi-plus" @click="open()">Hinzufügen</v-btn></template></PageHeader><v-progress-linear v-if="query.isPending.value" indeterminate /><v-alert v-else-if="query.isError.value" type="error">Daten konnten nicht geladen werden.</v-alert><v-table v-else><thead><tr><th>Name</th><th>Beschreibung</th><th>Status</th><th class="text-right">Aktionen</th></tr></thead><tbody><tr v-for="item in query.data.value" :key="item.id"><td>{{ item.name }}</td><td>{{ item.description }}</td><td><v-chip :color="item.isActive === false ? 'default' : 'success'" size="small">{{ item.isActive === false ? 'Inaktiv' : 'Aktiv' }}</v-chip></td><td class="text-right"><v-btn icon="mdi-pencil" variant="text" size="small" :aria-label="`${item.name} bearbeiten`" title="Bearbeiten" @click="open(item)" /><v-btn icon="mdi-delete" variant="text" size="small" color="error" :aria-label="`${item.name} löschen`" title="Löschen" @click="deleting = item" /></td></tr></tbody></v-table>
  <v-dialog v-model="dialog" max-width="550"><v-card :title="editing ? 'Eintrag bearbeiten' : 'Eintrag anlegen'"><v-form @submit.prevent="form.name && save()"><v-card-text><v-text-field v-model="form.name" label="Name" /><v-textarea v-model="form.description" label="Beschreibung" /><v-switch v-model="form.isActive" label="Aktiv" /></v-card-text><v-card-actions><v-spacer /><v-btn @click="dialog = false">Abbrechen</v-btn><v-btn type="submit" color="primary" variant="flat" :disabled="!form.name">Speichern</v-btn></v-card-actions></v-form></v-card></v-dialog><ConfirmDialog :model-value="!!deleting" title="Eintrag löschen?" text="Diese Aktion kann nicht rückgängig gemacht werden." @update:model-value="!$event && (deleting = null)" @confirm="remove" />
</template>
