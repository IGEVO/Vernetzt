<script setup lang="ts">
import { computed } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import AvatarImage from '@/components/AvatarImage.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useSearchList } from '@/composables/useSearchList'
import { api, toQuery } from '@/services/api'
import { initialsOf } from '@/services/avatars'
import type { ContactDto, ContactOrganizationDto, PagedResult } from '@/types/api'
import { organizationPath } from '@/services/organizationPath'

const { search, debouncedSearch, page } = useSearchList()
const query = useQuery({
  queryKey: computed(() => ['contacts', debouncedSearch.value, page.value]),
  queryFn: ({ signal }) => api.get<PagedResult<ContactDto>>(`/api/users/contacts/search${toQuery({ searchTerm: debouncedSearch.value, page: page.value, pageSize: 12 })}`, signal),
  placeholderData: keepPreviousData
})
const isDepartment = (org: ContactOrganizationDto) => org.type === 1 || org.type === 'Department'
const orgLink = (org: ContactOrganizationDto) => `/${isDepartment(org) ? 'departments' : 'clubs'}/${org.organizationId}`
const orgLabel = (org: ContactOrganizationDto) => isDepartment(org)
  ? organizationPath({ clubName: org.parentName, parentDepartmentName: org.parentDepartmentName, departmentName: org.organizationName })
  : org.organizationName
</script>

<template>
  <PageHeader title="Kontakte" subtitle="Ansprechpersonen der Vereine, Abteilungen und Gruppen" />
  <v-text-field v-model="search" label="Kontakte suchen" placeholder="Name, Verein, Abteilung oder Position" prepend-inner-icon="mdi-magnify" clearable hide-details class="mb-6" />
  <v-row v-if="query.isPending.value"><v-col v-for="n in 6" :key="n" cols="12" sm="6" lg="4"><v-skeleton-loader type="list-item-avatar-two-line, list-item-two-line" /></v-col></v-row>
  <v-alert v-else-if="query.isError.value" type="error">Kontakte konnten nicht geladen werden.</v-alert>
  <v-empty-state v-else-if="!query.data.value?.items.length" :title="debouncedSearch ? 'Keine Kontakte gefunden' : 'Noch keine Kontakte'" :text="debouncedSearch ? 'Versuche einen anderen Suchbegriff.' : undefined" icon="mdi-card-account-details-outline" />
  <template v-else>
    <p class="text-body-medium text-medium-emphasis mb-3">{{ query.data.value.totalCount }} {{ query.data.value.totalCount === 1 ? 'Kontakt' : 'Kontakte' }}</p>
    <v-row>
      <v-col v-for="contact in query.data.value.items" :key="contact.id" cols="12" sm="6" lg="4">
        <v-card height="100%" class="d-flex flex-column">
          <v-card-item>
            <template #prepend><AvatarImage type="users" :id="contact.id" :has-avatar="contact.hasAvatar" :initials="initialsOf(contact.firstName, contact.lastName)" :size="48" class="mr-1" /></template>
            <v-card-title>{{ contact.fullName ?? `${contact.firstName} ${contact.lastName}` }}</v-card-title>
            <v-card-subtitle v-if="contact.organizations[0]?.positionName">{{ contact.organizations[0].positionName }}</v-card-subtitle>
          </v-card-item>
          <v-card-text class="pt-1 flex-grow-1">
            <div v-if="contact.organizations.length" class="d-flex flex-wrap ga-1 mb-3">
              <v-chip v-for="org in contact.organizations" :key="`${org.organizationId}-${org.positionId ?? ''}`" :to="orgLink(org)" size="small" :prepend-icon="isDepartment(org) ? 'mdi-source-branch' : 'mdi-account-group'" :title="org.positionName ? `${orgLabel(org)} · ${org.positionName}` : orgLabel(org)">
                {{ orgLabel(org) }}
              </v-chip>
            </div>
            <div v-if="contact.email" class="d-flex align-center ga-2 mb-1 min-w-0"><v-icon icon="mdi-email-outline" size="small" aria-hidden="true" /><a :href="`mailto:${contact.email}`" class="text-truncate">{{ contact.email }}</a></div>
            <div v-if="contact.phoneNumber" class="d-flex align-center ga-2"><v-icon icon="mdi-phone-outline" size="small" aria-hidden="true" /><a :href="`tel:${contact.phoneNumber}`">{{ contact.phoneNumber }}</a></div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
    <v-pagination v-if="query.data.value.totalPages > 1" v-model="page" :length="query.data.value.totalPages" class="mt-6" />
  </template>
</template>
