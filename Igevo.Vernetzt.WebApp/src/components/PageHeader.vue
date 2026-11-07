<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'

/**
 * Uniform page header: optional breadcrumbs, the page's h1 (focused after navigation) and actions on the right.
 * The `subtitle` slot replaces the plain subtitle when it needs links (e.g. the organization hierarchy).
 */
defineProps<{ title: string; subtitle?: string; breadcrumbs?: Array<{ title: string; to?: RouteLocationRaw }> }>()
</script>

<template>
  <header class="page-header d-flex flex-wrap align-center ga-3 mb-6">
    <div class="page-header__text">
      <v-breadcrumbs v-if="breadcrumbs?.length" :items="breadcrumbs" density="compact" class="pa-0 mb-1" />
      <div class="d-flex align-center ga-3">
        <slot name="prepend" />
        <div>
          <h1 class="text-headline-large page-title" tabindex="-1">{{ title }}</h1>
          <p v-if="$slots.subtitle" class="text-body-large text-medium-emphasis mb-0"><slot name="subtitle" /></p>
          <p v-else-if="subtitle" class="text-body-large text-medium-emphasis mb-0">{{ subtitle }}</p>
        </div>
      </div>
    </div>
    <v-spacer />
    <div v-if="$slots.actions" class="d-flex flex-wrap align-center ga-2"><slot name="actions" /></div>
  </header>
</template>

<style scoped>
.page-header__text { min-width: 0; }
.page-header h1 { overflow-wrap: anywhere; }
</style>
