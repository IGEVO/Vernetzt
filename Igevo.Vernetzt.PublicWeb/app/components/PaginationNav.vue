<script setup lang="ts">
defineProps<{ page: number; totalPages: number }>()
const route = useRoute()
const pageLink = (page: number) => ({ path: route.path, query: { ...route.query, page: page > 1 ? String(page) : undefined } })
</script>

<template>
  <nav v-if="totalPages > 1" class="pagination" aria-label="Seiten">
    <NuxtLink v-if="page > 1" class="btn btn-quiet" :to="pageLink(page - 1)" rel="prev">Vorherige</NuxtLink>
    <span v-else />
    <span>Seite {{ page }} von {{ totalPages }}</span>
    <NuxtLink v-if="page < totalPages" class="btn btn-quiet" :to="pageLink(page + 1)" rel="next">Nächste</NuxtLink>
    <span v-else />
  </nav>
</template>
