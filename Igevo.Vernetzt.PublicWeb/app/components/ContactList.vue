<script setup lang="ts">
import type { PublicContact } from '~/types/public'

defineProps<{ contacts?: readonly PublicContact[] | null }>()
</script>

<template>
  <ul v-if="contacts?.length" class="contacts">
    <li v-for="contact in contacts" :key="contact.id" class="contact">
      <h3>{{ contact.firstName }} {{ contact.lastName }}</h3>
      <p v-if="contact.positionName" class="role">{{ contact.positionName }}</p>
      <ContactReveal
        :source="`/api/public/v1/contacts/${contact.id}/contact-details`"
        :has-email="contact.hasEmail" :has-phone-number="contact.hasPhoneNumber"
        :owner="`${contact.firstName} ${contact.lastName}`"
      />
    </li>
  </ul>
  <p v-else class="empty">Keine öffentlichen Ansprechpersonen eingetragen.</p>
</template>
