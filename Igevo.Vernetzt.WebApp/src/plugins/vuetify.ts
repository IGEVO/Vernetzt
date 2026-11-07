import '@mdi/font/css/materialdesignicons.css'
import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { de } from 'vuetify/locale'

export type ThemePreference = 'System' | 'Light' | 'Dark'

export function storedThemePreference(): ThemePreference {
  const stored = localStorage.getItem('themePreference')
  return stored === 'Light' || stored === 'Dark' ? stored : 'System'
}

export function effectiveTheme(preference = storedThemePreference()): 'vernetztLight' | 'vernetztDark' {
  return preference === 'Dark' || (preference === 'System' && matchMedia('(prefers-color-scheme: dark)').matches)
    ? 'vernetztDark'
    : 'vernetztLight'
}

export const vuetify = createVuetify({
  components,
  directives,
  locale: { locale: 'de', fallback: 'de', messages: { de } },
  theme: {
    defaultTheme: effectiveTheme(),
    themes: {
      vernetztLight: {
        dark: false,
        colors: { primary: '#0D4A70', secondary: '#F59E0B', brand: '#0D4A70', 'on-brand': '#FFFFFF', background: '#F5F5F5', surface: '#FFFFFF', 'on-surface-variant': '#595959', success: '#10B981', info: '#3B82F6', warning: '#FB923C', error: '#EF4444' }
      },
      vernetztDark: {
        dark: true,
        colors: { primary: '#90CAF9', 'on-primary': '#092033', secondary: '#F59E0B', brand: '#0D4A70', 'on-brand': '#FFFFFF', background: '#121212', surface: '#1E1E1E', 'on-surface-variant': '#D5D5D5', success: '#34D399', info: '#7DD3FC', warning: '#FDBA74', error: '#F87171' }
      }
    }
  },
  defaults: {
    VTextField: { variant: 'outlined', density: 'comfortable' },
    VTextarea: { variant: 'outlined', density: 'comfortable' },
    VSelect: { variant: 'outlined', density: 'comfortable' },
    VAutocomplete: { variant: 'outlined', density: 'comfortable' },
    VBtn: { rounded: 'lg' },
    VCard: { rounded: 'lg' }
  }
})
