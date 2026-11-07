<script setup lang="ts">
import AvatarImage from '@/components/AvatarImage.vue'
import { initialsOf } from '@/services/avatars'
import type { DepartmentSummaryDto } from '@/types/api'

/** Card grid for the departments of a club or the groups of a department. */
defineProps<{ items: DepartmentSummaryDto[]; emptyText: string }>()

function subtitle(item: DepartmentSummaryDto): string {
  const contacts = `${item.contactPersonsCount} ${item.contactPersonsCount === 1 ? 'Kontaktperson' : 'Kontaktpersonen'}`
  const groups = item.groupCount ? `${item.groupCount} ${item.groupCount === 1 ? 'Gruppe' : 'Gruppen'}` : null
  return [item.city, groups, contacts].filter(Boolean).join(' · ')
}

/** First line of the info text as a teaser (e.g. training times). */
function teaser(item: DepartmentSummaryDto): string | null {
  return item.description?.split('\n').map((line) => line.trim()).find(Boolean) ?? null
}
</script>

<template>
  <v-row v-if="items.length">
    <v-col v-for="item in items" :key="item.id" cols="12" sm="6" lg="4">
      <v-card :to="`/departments/${item.id}`" height="100%" hover>
        <v-card-item>
          <template #prepend><AvatarImage type="departments" :id="item.id" :has-avatar="item.hasAvatar" :initials="initialsOf(item.name)" :size="40" class="mr-1" /></template>
          <v-card-title>{{ item.name }}</v-card-title>
          <v-card-subtitle>{{ subtitle(item) }}</v-card-subtitle>
        </v-card-item>
        <v-card-text v-if="teaser(item)" class="pt-0 text-body-medium text-medium-emphasis teaser">{{ teaser(item) }}</v-card-text>
      </v-card>
    </v-col>
  </v-row>
  <v-card v-else variant="outlined"><v-card-text class="text-medium-emphasis">{{ emptyText }}</v-card-text></v-card>
</template>

<style scoped>
.teaser { overflow: hidden; text-overflow: ellipsis; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; }
</style>
