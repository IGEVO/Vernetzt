<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/vue-query'
import AvatarImage from '@/components/AvatarImage.vue'
import OrganizationFormDialog from '@/components/OrganizationFormDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useSearchList } from '@/composables/useSearchList'
import { api, toQuery } from '@/services/api'
import { initialsOf } from '@/services/avatars'
import { useAuthStore } from '@/stores/auth'
import type { ClubSummaryDto, PagedResult } from '@/types/api'

const router = useRouter(); const auth = useAuthStore(); const client = useQueryClient()
const { search, debouncedSearch, page } = useSearchList()
const dialog = ref(false)
const canCreate = computed(() => auth.hasRole('SystemAdmin', 'Manager'))
const query = useQuery({
  queryKey: computed(() => ['clubs', debouncedSearch.value, page.value]),
  queryFn: ({ signal }) => api.get<PagedResult<ClubSummaryDto>>(`/api/clubs/search${toQuery({ searchTerm: debouncedSearch.value, page: page.value, pageSize: 12 })}`, signal),
  placeholderData: keepPreviousData
})
async function created(club: { id: string }): Promise<void> {
  await client.invalidateQueries({ queryKey: ['clubs'] })
  await router.push(`/clubs/${club.id}`)
}
const departmentLabel = (count: number) => `${count} ${count === 1 ? 'Abteilung' : 'Abteilungen'}`
</script>

<template>
  <PageHeader title="Vereine">
    <template v-if="canCreate" #actions><v-btn color="primary" prepend-icon="mdi-plus" @click="dialog = true">Verein anlegen</v-btn></template>
  </PageHeader>
  <v-text-field v-model="search" label="Vereine suchen" placeholder="Name, Ort, Abteilung oder Gruppe" prepend-inner-icon="mdi-magnify" clearable hide-details class="mb-6" />
  <v-row v-if="query.isPending.value"><v-col v-for="n in 6" :key="n" cols="12" sm="6" lg="4"><v-skeleton-loader type="list-item-avatar-two-line, list-item-two-line" /></v-col></v-row>
  <v-alert v-else-if="query.isError.value" type="error">Vereine konnten nicht geladen werden.</v-alert>
  <v-empty-state v-else-if="!query.data.value?.items.length" :title="debouncedSearch ? 'Keine Vereine gefunden' : 'Noch keine Vereine'" :text="debouncedSearch ? 'Versuche einen anderen Suchbegriff.' : undefined" icon="mdi-account-group-outline" />
  <template v-else>
    <p class="text-body-medium text-medium-emphasis mb-3">{{ query.data.value.totalCount }} {{ query.data.value.totalCount === 1 ? 'Verein' : 'Vereine' }}</p>
    <v-row>
      <v-col v-for="club in query.data.value.items" :key="club.id" cols="12" sm="6" lg="4">
        <v-card :to="`/clubs/${club.id}`" height="100%" hover class="d-flex flex-column">
          <v-card-item>
            <template #prepend><AvatarImage type="clubs" :id="club.id" :has-avatar="club.hasAvatar" :initials="initialsOf(club.name)" :size="48" class="mr-1" /></template>
            <v-card-title>{{ club.name }}</v-card-title>
            <v-card-subtitle v-if="club.clubTypeName">{{ club.clubTypeName }}</v-card-subtitle>
          </v-card-item>
          <v-card-text class="pt-1 flex-grow-1">
            <div v-if="club.city" class="d-flex align-center ga-2 mb-2"><v-icon icon="mdi-map-marker-outline" size="small" aria-hidden="true" />{{ club.city }}</div>
            <div v-if="club.matchingDepartments?.length" class="mb-2">
              <div class="text-body-small text-medium-emphasis mb-1">Treffer in Abteilungen und Gruppen</div>
              <div class="d-flex flex-wrap ga-1">
                <v-chip v-for="department in club.matchingDepartments" :key="department.id" size="small" color="primary" variant="tonal" prepend-icon="mdi-source-branch">{{ department.parentDepartmentName ? `${department.parentDepartmentName} › ${department.name}` : department.name }}</v-chip>
              </div>
            </div>
            <v-chip v-else-if="club.departmentCount" size="small" variant="tonal" prepend-icon="mdi-source-branch">{{ departmentLabel(club.departmentCount) }}</v-chip>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
    <v-pagination v-if="query.data.value.totalPages > 1" v-model="page" :length="query.data.value.totalPages" class="mt-6" />
  </template>
  <OrganizationFormDialog v-model="dialog" kind="club" @saved="created" />
</template>
