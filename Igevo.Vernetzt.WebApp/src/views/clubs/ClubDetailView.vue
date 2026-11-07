<script setup lang="ts">
import { computed, defineAsyncComponent, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ContactAssignmentsPanel from '@/components/ContactAssignmentsPanel.vue'
import DepartmentCards from '@/components/DepartmentCards.vue'
import OrganizationFormDialog from '@/components/OrganizationFormDialog.vue'
import OrganizationOverview from '@/components/OrganizationOverview.vue'
import OrganizationTabs from '@/components/OrganizationTabs.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useOrganizationTab } from '@/composables/useOrganizationTab'
import { api } from '@/services/api'
import { publicWebLink } from '@/services/publicWeb'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { ClubDto } from '@/types/api'

const route = useRoute(); const router = useRouter(); const ui = useUiStore(); const client = useQueryClient()
const id = computed(() => String(route.params.id))
const EntityEventsPanel = defineAsyncComponent(() => import('@/views/events/EntityEventsView.vue'))
const activeSection = useOrganizationTab()
const editDialog = ref(false); const departmentDialog = ref(false); const deleteDialog = ref(false)
const query = useQuery({ queryKey: computed(() => ['club', id.value]), queryFn: ({ signal }) => api.get<ClubDto>(`/api/clubs/${id.value}`, signal) })
const club = computed(() => query.data.value)
const auth = useAuthStore()
const publicUrl = computed(() => club.value ? publicWebLink(auth.publicWebUrl, 'vereine', club.value.id, club.value.name) : null)

async function saved(): Promise<void> { await Promise.all([query.refetch(), client.invalidateQueries({ queryKey: ['clubs'] })]) }
async function departmentCreated(department: { id: string }): Promise<void> { await saved(); await router.push(`/departments/${department.id}`) }
async function remove(): Promise<void> {
  try { await api.delete(`/api/clubs/${id.value}`); await client.invalidateQueries({ queryKey: ['clubs'] }); ui.notify('Verein wurde gelöscht.', 'success'); await router.replace('/clubs') }
  catch { ui.notify('Verein konnte nicht gelöscht werden.', 'error') }
}
</script>

<template>
  <v-progress-linear v-if="query.isPending.value" indeterminate color="primary" />
  <v-alert v-else-if="query.isError.value" type="error">Der Verein konnte nicht geladen werden.</v-alert>
  <template v-else-if="club">
    <PageHeader :title="club.name" :breadcrumbs="[{ title: 'Vereine', to: '/clubs' }, { title: club.name }]">
      <template #actions>
        <v-btn v-if="publicUrl" color="primary" variant="tonal" prepend-icon="mdi-open-in-new" :href="publicUrl" target="_blank" rel="noopener" title="Öffentliche Seite in neuem Tab öffnen">Öffentliche Seite</v-btn>
        <v-btn v-if="club.canDelete" color="error" variant="text" icon="mdi-delete" aria-label="Verein löschen" title="Verein löschen" @click="deleteDialog = true" />
      </template>
    </PageHeader>
    <OrganizationTabs v-model="activeSection" :contact-count="club.contactAssignments.length" />
    <template v-if="activeSection === 'overview'">
      <OrganizationOverview type="clubs" :id="id" :name="club.name" :has-avatar="club.hasAvatar" :editable="club.canEdit" :type-name="club.clubTypeName" :description="club.description" :street="club.street" :postal-code="club.postalCode" :city="club.city" :email="club.email" :website="club.websiteUrl" :social-media-links="club.socialMediaLinks" @edit="editDialog = true" />
      <div class="d-flex align-center mt-8 mb-3">
        <h2 class="text-headline-small">Abteilungen</h2>
        <v-spacer />
        <v-btn v-if="club.canEdit" variant="tonal" prepend-icon="mdi-plus" @click="departmentDialog = true">Abteilung anlegen</v-btn>
      </div>
      <DepartmentCards :items="club.departments" empty-text="Dieser Verein hat noch keine Abteilungen." />
    </template>
    <ContactAssignmentsPanel v-else-if="activeSection === 'contacts'" entity-type="clubs" :entity-id="id" :assignments="club.contactAssignments" :can-manage="club.canManageContacts" @changed="query.refetch()" />
    <EntityEventsPanel v-else entity-type="club" />

    <OrganizationFormDialog v-model="editDialog" kind="club" :organization="club" @saved="saved" />
    <OrganizationFormDialog v-model="departmentDialog" kind="department" :club-id="id" @saved="departmentCreated" />
    <ConfirmDialog v-model="deleteDialog" title="Verein löschen?" :text="`„${club.name}“ wird gelöscht. Diese Aktion kann nicht rückgängig gemacht werden.`" @confirm="remove" />
  </template>
</template>
