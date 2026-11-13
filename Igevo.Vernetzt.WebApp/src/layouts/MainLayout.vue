<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useDisplay } from 'vuetify'
import { useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { api } from '@/services/api'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { SupportContactDto, VersionInfo } from '@/types/api'
import AvatarImage from '@/components/AvatarImage.vue'
import { initialsOf } from '@/services/avatars'
import { USER_DOCS_URL } from '@/services/userDocs'

const auth = useAuthStore()
const ui = useUiStore()
const router = useRouter()
const { mobile } = useDisplay()
const version = ref('')
const admin = computed(() => auth.hasRole('SystemAdmin', 'Manager'))
const systemAdmin = computed(() => auth.hasRole('SystemAdmin'))
const fullName = computed(() => `${auth.user?.firstName ?? ''} ${auth.user?.lastName ?? ''}`.trim())
const helpOpen = ref(false)
// Loaded on first opening of the help menu, so regular page loads don't need the extra request.
const supportContact = useQuery({ queryKey: ['support', 'contact'], queryFn: ({ signal }) => api.get<SupportContactDto>('/api/support/contact', signal), enabled: helpOpen, staleTime: 5 * 60_000 })
const contactMailto = computed(() => {
  const email = supportContact.data.value?.email?.trim()
  return email ? `mailto:${email}?subject=${encodeURIComponent('Frage zu Vernetzt')}` : ''
})
const themes = [{ value: 'System', title: 'Wie System', icon: 'mdi-theme-light-dark' }, { value: 'Light', title: 'Hell', icon: 'mdi-white-balance-sunny' }, { value: 'Dark', title: 'Dunkel', icon: 'mdi-weather-night' }] as const

onMounted(async () => {
  try { const result = await api.get<VersionInfo>('/api/version'); version.value = result.userDisplayVersion ?? result.version } catch { /* non-critical */ }
})

async function logout(): Promise<void> { await auth.logout(); await router.replace('/login') }
</script>

<template>
  <v-layout>
    <v-app-bar color="brand" :density="mobile ? 'compact' : 'default'" elevation="2">
      <v-app-bar-nav-icon aria-label="Navigation umschalten" @click="ui.drawer = !ui.drawer" />
      <v-app-bar-title>
        <RouterLink to="/" class="brand-link"><v-img src="/images/logo-no-bg.svg" width="32" height="32" alt="" /><span>Vernetzt</span></RouterLink>
      </v-app-bar-title>
      <v-menu v-model="helpOpen" location="bottom end">
        <template #activator="{ props }"><v-btn v-bind="props" icon="mdi-help-circle-outline" aria-label="Hilfe" title="Hilfe" /></template>
        <v-list min-width="260">
          <v-list-subheader>Hilfe</v-list-subheader>
          <v-list-item :href="USER_DOCS_URL" target="_blank" rel="noopener noreferrer" prepend-icon="mdi-book-open-variant" title="Hilfe öffnen" append-icon="mdi-open-in-new" />
          <v-list-item v-if="contactMailto" :href="contactMailto" prepend-icon="mdi-email-outline" title="Ansprechpartner kontaktieren" :subtitle="supportContact.data.value?.name || supportContact.data.value?.email" />
          <v-list-item v-else-if="supportContact.isPending.value" prepend-icon="mdi-email-outline" title="Ansprechpartner wird geladen …" disabled />
        </v-list>
      </v-menu>
      <v-menu>
        <template #activator="{ props }"><v-btn v-bind="props" :icon="ui.dark ? 'mdi-weather-night' : 'mdi-white-balance-sunny'" aria-label="Darstellung auswählen" title="Darstellung" /></template>
        <v-list density="compact" :selected="[ui.preference]" color="primary">
          <v-list-subheader>Darstellung</v-list-subheader>
          <v-list-item v-for="item in themes" :key="item.value" :value="item.value" :prepend-icon="item.icon" :title="item.title" @click="ui.setTheme(item.value)" />
        </v-list>
      </v-menu>
      <v-menu location="bottom end">
        <template #activator="{ props }">
          <v-btn v-bind="props" icon aria-label="Benutzermenü" title="Mein Konto" class="mr-1">
            <AvatarImage type="users" :has-avatar="auth.user?.hasAvatar" :initials="initialsOf(auth.user?.firstName, auth.user?.lastName)" :size="32" color="secondary" />
          </v-btn>
        </template>
        <v-list min-width="260">
          <v-list-item :title="fullName" :subtitle="auth.user?.email">
            <template #prepend><AvatarImage type="users" :has-avatar="auth.user?.hasAvatar" :initials="initialsOf(auth.user?.firstName, auth.user?.lastName)" class="mr-4" /></template>
          </v-list-item>
          <v-divider class="my-1" />
          <v-list-item to="/konto" prepend-icon="mdi-account-cog" title="Mein Konto" />
          <v-list-item prepend-icon="mdi-logout" title="Abmelden" @click="logout" />
        </v-list>
      </v-menu>
    </v-app-bar>

    <v-navigation-drawer v-model="ui.drawer" :temporary="mobile" :rail="!mobile && !ui.drawer" expand-on-hover>
      <v-list nav>
        <v-list-item to="/" exact prepend-icon="mdi-view-dashboard" title="Dashboard" />
        <v-list-item to="/clubs" prepend-icon="mdi-account-group" title="Vereine &amp; Organisationen" />
        <v-list-item to="/kontakte" prepend-icon="mdi-card-account-details" title="Kontakte" />
        <v-list-item to="/emails/send" prepend-icon="mdi-email" title="E-Mail versenden" />
        <v-list-item to="/calendar/main" prepend-icon="mdi-calendar-month" title="Kalender" />
        <v-list-group v-if="admin" value="admin">
          <template #activator="{ props }"><v-list-item v-bind="props" prepend-icon="mdi-shield-account" title="Administration" /></template>
          <v-list-item to="/admin/club-types" title="Vereinstypen" />
          <v-list-item to="/admin/contact-positions" title="Kontaktpositionen" />
          <v-list-item to="/admin/holidays" title="Ferien & Feiertage" />
          <v-list-item to="/admin/users" title="Benutzerverwaltung" />
          <v-list-item to="/admin/pending" title="Ausstehende Bestätigungen" />
          <v-list-item v-if="systemAdmin" to="/admin/audit-log" title="Audit-Log" />
          <v-list-item v-if="systemAdmin" to="/admin/settings" title="Systemeinstellungen" />
        </v-list-group>
      </v-list>
      <template v-if="version" #append><div v-show="ui.drawer || mobile" class="text-body-small text-medium-emphasis pa-4">Version {{ version }}</div></template>
    </v-navigation-drawer>

    <v-main><v-container max-width="1440" class="page-container"><RouterView /></v-container></v-main>
  </v-layout>
</template>

<style scoped>
.brand-link { align-items: center; color: white; display: inline-flex; gap: 10px; text-decoration: none; }
</style>
