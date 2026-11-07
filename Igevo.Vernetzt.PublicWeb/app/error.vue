<script setup lang="ts">
const props = defineProps<{ error: { statusCode?: number; statusMessage?: string } }>()
const config = useRuntimeConfig()
const notFound = computed(() => props.error.statusCode === 404)
useSeoMeta({ title: () => notFound.value ? 'Nicht gefunden' : 'Fehler', robots: 'noindex' })
</script>

<template>
  <div class="app-shell">
      <header class="site-header">
        <div class="container">
          <NuxtLink to="/" class="brand">
            <img src="/logo.svg" alt="" width="30" height="30">
            <span>{{ config.public.siteName }}</span>
          </NuxtLink>
        </div>
      </header>
      <main id="main-content" class="container narrow page">
        <template v-if="notFound">
          <h1>Seite nicht gefunden</h1>
          <p class="muted" style="margin-top:.75rem">
            Der Eintrag wurde entfernt oder die Adresse ist falsch. Über die Suche findest du alle Vereine und Termine.
          </p>
        </template>
        <template v-else>
          <h1>Die Seite kann gerade nicht geladen werden</h1>
          <p class="muted" style="margin-top:.75rem">Bitte versuche es in ein paar Minuten noch einmal.</p>
        </template>
        <div style="display:flex;flex-wrap:wrap;gap:.75rem;margin-top:1.5rem">
          <NuxtLink class="btn btn-primary" to="/vereine">Vereine durchsuchen</NuxtLink>
          <NuxtLink class="btn btn-quiet" to="/veranstaltungen">Termine ansehen</NuxtLink>
        </div>
      </main>
    </div>
</template>
