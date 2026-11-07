<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PageHeader from '@/components/PageHeader.vue'
import ProfilePanel from '@/components/account/ProfilePanel.vue'
import AssignmentsPanel from '@/components/account/AssignmentsPanel.vue'
import SecurityPanel from '@/components/account/SecurityPanel.vue'
import PrivacyPanel from '@/components/account/PrivacyPanel.vue'
import AddressBookPanel from '@/components/account/AddressBookPanel.vue'
import { useAuthStore } from '@/stores/auth'

/** "Mein Konto": profile, own assignments, sign-in security and privacy on one page (formerly "Mein Profil" + "Persönliche Einstellungen"). */
const tabs = [
  { value: 'profil', title: 'Profil', icon: 'mdi-account' },
  { value: 'zuordnungen', title: 'Zuordnungen', icon: 'mdi-account-group' },
  { value: 'sicherheit', title: 'Anmeldung & Sicherheit', icon: 'mdi-shield-key' },
  { value: 'adressbuch', title: 'Adressbuch', icon: 'mdi-card-account-phone' },
  { value: 'datenschutz', title: 'Datenschutz', icon: 'mdi-shield-account' }
] as const
type Tab = typeof tabs[number]['value']

const route = useRoute(); const router = useRouter(); const auth = useAuthStore()
const tab = computed<Tab>({
  get: () => tabs.find((item) => item.value === route.query.tab)?.value ?? 'profil',
  set: (value) => { void router.replace({ query: value === 'profil' ? {} : { tab: value } }) }
})
</script>

<template>
  <PageHeader title="Mein Konto" :subtitle="auth.user?.email" />
  <v-tabs v-model="tab" color="primary" class="tab-bar mb-6" show-arrows aria-label="Bereiche des Kontos">
    <v-tab v-for="item in tabs" :key="item.value" :value="item.value" :prepend-icon="item.icon">{{ item.title }}</v-tab>
  </v-tabs>
  <ProfilePanel v-if="tab === 'profil'" />
  <AssignmentsPanel v-else-if="tab === 'zuordnungen'" />
  <SecurityPanel v-else-if="tab === 'sicherheit'" />
  <AddressBookPanel v-else-if="tab === 'adressbuch'" />
  <PrivacyPanel v-else />
</template>
