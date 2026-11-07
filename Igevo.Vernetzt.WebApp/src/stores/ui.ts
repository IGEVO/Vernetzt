import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { useTheme } from 'vuetify'
import { effectiveTheme, storedThemePreference, type ThemePreference } from '@/plugins/vuetify'

export const useUiStore = defineStore('ui', () => {
  // Open by default only on desktop; on mobile the drawer is a temporary overlay (Vuetify mobile breakpoint: < 1280px).
  const drawer = ref(typeof window === 'undefined' || window.matchMedia('(min-width: 1280px)').matches)
  const preference = ref<ThemePreference>(storedThemePreference())
  const snackbar = ref({ open: false, text: '', color: 'info' })
  const updateAvailable = ref(false)
  const theme = useTheme()

  function applyTheme(): void { theme.global.name.value = effectiveTheme(preference.value) }
  function setTheme(value: ThemePreference): void {
    preference.value = value
    localStorage.setItem('themePreference', value)
    applyTheme()
  }
  function notify(text: string, color: 'success' | 'info' | 'warning' | 'error' = 'info'): void {
    snackbar.value = { open: true, text, color }
  }

  const dark = computed(() => theme.global.current.value.dark)
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { if (preference.value === 'System') applyTheme() })
  return { drawer, preference, snackbar, updateAvailable, dark, setTheme, notify }
})
