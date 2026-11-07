<script setup lang="ts">
const model = defineModel<'overview' | 'contacts' | 'events'>({ required: true })
defineProps<{ contactCount?: number }>()
</script>

<template>
  <v-tabs v-model="model" color="primary" class="tab-bar mb-6" show-arrows aria-label="Bereiche der Organisation">
    <v-tab value="overview" prepend-icon="mdi-information-outline">Übersicht</v-tab>
    <v-tab value="contacts" prepend-icon="mdi-card-account-details">
      Kontaktpersonen
      <v-badge v-if="contactCount" :content="contactCount" color="primary" inline />
    </v-tab>
    <v-tab value="events" prepend-icon="mdi-calendar-month">Termine</v-tab>
  </v-tabs>
</template>
