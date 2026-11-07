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
import { departmentKind } from '@/services/organizationPath'
import { publicWebLink } from '@/services/publicWeb'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { DepartmentDto } from '@/types/api'

/** Detail page for departments and groups (a group is a department below a department). */
const route = useRoute(); const router = useRouter(); const ui = useUiStore(); const client = useQueryClient()
const id = computed(() => String(route.params.id))
const EntityEventsPanel = defineAsyncComponent(() => import('@/views/events/EntityEventsView.vue'))
const activeSection = useOrganizationTab()
const editDialog = ref(false); const deleteDialog = ref(false); const groupDialog = ref(false)
const query = useQuery({ queryKey: computed(() => ['department', id.value]), queryFn: ({ signal }) => api.get<DepartmentDto>(`/api/departments/${id.value}`, signal) })
const department = computed(() => query.data.value)
const auth = useAuthStore()
const publicUrl = computed(() => department.value ? publicWebLink(auth.publicWebUrl, 'abteilungen', department.value.id, department.value.name) : null)
const kind = computed(() => departmentKind(department.value?.isGroup))
const clubLink = computed(() => department.value ? `/clubs/${department.value.clubId}` : '')
const parentLink = computed(() => department.value?.parentDepartmentId ? `/departments/${department.value.parentDepartmentId}` : '')
const breadcrumbs = computed(() => {
  const item = department.value
  if (!item) return []
  return [
    { title: 'Vereine', to: '/clubs' },
    { title: item.clubName ?? 'Verein', to: clubLink.value },
    ...(item.parentDepartmentId ? [{ title: item.parentDepartmentName ?? 'Abteilung', to: parentLink.value }] : []),
    { title: item.name }
  ]
})

async function saved(): Promise<void> {
  await Promise.all([query.refetch(), client.invalidateQueries({ queryKey: ['club'] }), client.invalidateQueries({ queryKey: ['department'] })])
}
async function groupCreated(group: { id: string }): Promise<void> { await saved(); await router.push(`/departments/${group.id}`) }
async function remove(): Promise<void> {
  const item = department.value
  const target = item?.parentDepartmentId ? `/departments/${item.parentDepartmentId}` : item?.clubId ? `/clubs/${item.clubId}` : '/clubs'
  try {
    await api.delete(`/api/departments/${id.value}`)
    await Promise.all([client.invalidateQueries({ queryKey: ['club'] }), client.invalidateQueries({ queryKey: ['department'] })])
    ui.notify(`${kind.value} wurde gelöscht.`, 'success')
    await router.replace(target)
  } catch { ui.notify(`${kind.value} konnte nicht gelöscht werden.`, 'error') }
}
const deleteText = computed(() => {
  const item = department.value
  if (!item) return ''
  const groups = item.groups.length ? ` Auch ${item.groups.length === 1 ? 'die Gruppe' : `die ${item.groups.length} Gruppen`} dieser Abteilung samt Terminen werden gelöscht.` : ''
  return `„${item.name}“ wird gelöscht.${groups} Diese Aktion kann nicht rückgängig gemacht werden.`
})
</script>

<template>
  <v-progress-linear v-if="query.isPending.value" indeterminate color="primary" />
  <v-alert v-else-if="query.isError.value" type="error">Die Abteilung oder Gruppe konnte nicht geladen werden.</v-alert>
  <template v-else-if="department">
    <PageHeader :title="department.name" :breadcrumbs="breadcrumbs">
      <template #subtitle>
        <span data-testid="organization-hierarchy">
          <template v-if="department.isGroup">
            Gruppe der Abteilung <RouterLink :to="parentLink">{{ department.parentDepartmentName }}</RouterLink>
            · <RouterLink :to="clubLink">{{ department.clubName }}</RouterLink>
          </template>
          <template v-else>Abteilung von <RouterLink :to="clubLink">{{ department.clubName }}</RouterLink></template>
        </span>
      </template>
      <template #actions>
        <v-btn v-if="publicUrl" color="primary" variant="tonal" prepend-icon="mdi-open-in-new" :href="publicUrl" target="_blank" rel="noopener" title="Öffentliche Seite in neuem Tab öffnen">Öffentliche Seite</v-btn>
        <v-btn v-if="department.canDelete" color="error" variant="text" icon="mdi-delete" :aria-label="`${kind} löschen`" :title="`${kind} löschen`" @click="deleteDialog = true" />
      </template>
    </PageHeader>
    <OrganizationTabs v-model="activeSection" :contact-count="department.contactAssignments.length" />
    <template v-if="activeSection === 'overview'">
      <OrganizationOverview
        type="departments" :id="id" :name="department.name" :has-avatar="department.hasAvatar" :editable="department.canEdit"
        :show-avatar="!department.isGroup" :description="department.description"
        :street="department.street" :postal-code="department.postalCode" :city="department.city" :email="department.email"
        :website="department.website" :social-media-links="department.socialMediaLinks" @edit="editDialog = true"
      />
      <template v-if="!department.isGroup">
        <div class="d-flex align-center mt-8 mb-3">
          <h2 class="text-headline-small">Gruppen</h2>
          <v-spacer />
          <v-btn v-if="department.canEdit" variant="tonal" prepend-icon="mdi-plus" @click="groupDialog = true">Gruppe anlegen</v-btn>
        </div>
        <p class="text-body-medium text-medium-emphasis mb-3">Mannschaften, Kurse oder Altersklassen mit eigenen Infos, Kontaktpersonen und Terminen.</p>
        <DepartmentCards :items="department.groups" empty-text="Diese Abteilung hat noch keine Gruppen." />
      </template>
    </template>
    <ContactAssignmentsPanel v-else-if="activeSection === 'contacts'" entity-type="departments" :entity-id="id" :assignments="department.contactAssignments" :can-manage="department.canManageContacts" :is-group="department.isGroup" @changed="query.refetch()" />
    <EntityEventsPanel v-else entity-type="department" />

    <OrganizationFormDialog v-model="editDialog" :kind="department.isGroup ? 'group' : 'department'" :organization="department" @saved="saved" />
    <OrganizationFormDialog v-if="!department.isGroup" v-model="groupDialog" kind="group" :club-id="department.clubId" :parent-department-id="department.id" @saved="groupCreated" />
    <ConfirmDialog v-model="deleteDialog" :title="`${kind} löschen?`" :text="deleteText" @confirm="remove" />
  </template>
</template>
