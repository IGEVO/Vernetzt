import { describe, expect, it } from 'vitest'
import { visibilityDisplay, visibilityOptions } from '@/services/visibility'

describe('visibilityDisplay', () => {
  it('zeigt Eingeschränkt mit Schloss-Symbol', () => {
    expect(visibilityDisplay(0)).toEqual({
      title: 'Eingeschränkt',
      icon: 'mdi-shield-lock-outline',
      color: 'secondary'
    })
  })

  it('zeigt registrierte Benutzer mit Gruppen-Symbol', () => {
    expect(visibilityDisplay(1)).toEqual({
      title: 'Alle registrierten Benutzer',
      icon: 'mdi-account-group-outline',
      color: 'primary'
    })
  })

  it('hebt öffentliche Sichtbarkeit als Warnung hervor', () => {
    expect(visibilityDisplay(2)).toEqual({ title: 'Öffentlich', icon: 'mdi-earth', color: 'warning' })
  })

  it('verwendet für jede Auswahl denselben Titel wie die Formularoptionen', () => {
    for (const option of visibilityOptions) {
      expect(visibilityDisplay(option.value).title).toBe(option.title)
    }
  })
})
