import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useUiStore } from '@/stores/ui'

const themeName = { value: '' }
vi.mock('vuetify', () => ({ useTheme: () => ({ global: { name: themeName, current: { value: { dark: themeName.value === 'vernetztDark' } } } }) }))
vi.mock('@/plugins/vuetify', () => ({
  storedThemePreference: () => 'System',
  effectiveTheme: (preference: string) => preference === 'Dark' ? 'vernetztDark' : 'vernetztLight'
}))

describe('ui store theme preference', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.matchMedia = vi.fn().mockReturnValue({ matches: false, addEventListener: vi.fn() }) as unknown as typeof window.matchMedia
  })

  it('remembers an explicit choice and forgets it again for "System"', () => {
    const ui = useUiStore()

    ui.setTheme('Dark')
    expect(localStorage.getItem('themePreference')).toBe('Dark')
    expect(themeName.value).toBe('vernetztDark')

    ui.setTheme('System')
    expect(localStorage.getItem('themePreference')).toBeNull()
    expect(ui.preference).toBe('System')
  })
})
