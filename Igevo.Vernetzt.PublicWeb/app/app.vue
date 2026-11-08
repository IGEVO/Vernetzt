<script setup lang="ts">
const config = useRuntimeConfig()
const { data: portal } = await usePortal()
const externalUrl = (page: 'imprint' | 'privacy') =>
  portal.value?.[page]?.mode === 'link' ? portal.value[page].url ?? null : null

useSeoMeta({
  titleTemplate: title => title ? `${title} – ${config.public.siteName}` : config.public.siteName,
  description: 'Vereine, Ansprechpersonen und öffentliche Termine.',
  ogSiteName: config.public.siteName,
  ogLocale: 'de_DE',
  twitterCard: 'summary_large_image'
})

// Apply a stored colour scheme before first paint. Without a stored choice the CSS follows the
// device setting, so server rendering needs no knowledge of it.
useHead({
  script: [{
    key: 'theme-init',
    tagPosition: 'head',
    innerHTML: "try{var t=localStorage.getItem('themePreference');if(t==='Light'||t==='Dark')document.documentElement.dataset.theme=t.toLowerCase()}catch(e){}"
  }]
})
</script>

<template>
  <div class="app-shell">
    <a class="skip-link" href="#main-content">Zum Inhalt springen</a>
    <header class="site-header">
      <div class="container">
        <NuxtLink to="/" class="brand">
          <img src="/logo.svg" alt="" width="30" height="30">
          <span>{{ config.public.siteName }}</span>
        </NuxtLink>
        <nav class="main-nav" aria-label="Hauptnavigation">
          <NuxtLink to="/vereine">Vereine &amp; Organisationen</NuxtLink>
          <NuxtLink to="/veranstaltungen">Termine</NuxtLink>
        </nav>
        <ThemeToggle />
      </div>
    </header>

    <aside v-if="portal?.notice" class="site-notice" :class="`is-${portal.notice.level}`" :aria-label="portal.notice.level === 'warning' ? 'Warnung' : 'Hinweis'">
      <div class="container">
        <AppIcon :name="portal.notice.level === 'warning' ? 'warning' : 'info'" />
        <p>{{ portal.notice.text }}</p>
      </div>
    </aside>

    <main id="main-content" tabindex="-1">
      <NuxtPage />
    </main>

    <footer class="site-footer">
      <div class="container">
        <span class="footer-brand">
          <img src="/logo.svg" alt="" width="22" height="22">
          {{ config.public.siteName }}
        </span>
        <nav class="footer-links" aria-label="Weitere Links">
          <a v-if="config.public.appUrl" :href="config.public.appUrl">Anmelden für Vereine</a>
          <a v-if="externalUrl('privacy')" :href="externalUrl('privacy')!" rel="noopener">Datenschutz</a>
          <NuxtLink v-else to="/datenschutz">Datenschutz</NuxtLink>
          <a v-if="externalUrl('imprint')" :href="externalUrl('imprint')!" rel="noopener">Impressum</a>
          <NuxtLink v-else to="/impressum">Impressum</NuxtLink>
        </nav>
      </div>
    </footer>
  </div>
</template>
