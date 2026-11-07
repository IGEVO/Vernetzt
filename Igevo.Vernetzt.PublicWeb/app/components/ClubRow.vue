<script setup lang="ts">
import type { PublicClubSummary } from '~/types/public'

const props = defineProps<{ club: PublicClubSummary }>()
const href = computed(() => entityPath('vereine', props.club.id ?? '', props.club.name ?? 'Verein'))
const meta = computed(() => {
  const parts = [props.club.clubTypeName].filter(Boolean)
  const count = props.club.departmentCount ?? 0
  if (count > 0) parts.push(count === 1 ? '1 Abteilung' : `${count} Abteilungen`)
  return parts.join(', ')
})
</script>

<template>
  <li>
    <NuxtLink :to="href" class="row">
      <span class="logo-box" :class="{ initial: !club.logoUrl }" aria-hidden="true">
        <img v-if="club.logoUrl" :src="club.logoUrl" alt="" loading="lazy" width="52" height="52">
        <template v-else>{{ club.name?.charAt(0) }}</template>
      </span>
      <span class="row-body">
        <span class="row-title">{{ club.name }}</span>
        <span v-if="meta" class="row-meta">{{ meta }}</span>
      </span>
    </NuxtLink>
  </li>
</template>
