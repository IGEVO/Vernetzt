import { describe, expect, it } from 'vitest'
import {
  effectiveContextEmailVisibilities, isStricterVisibility, restrictContextEmailVisibility, stricterVisibility,
  visibilityDisplay, visibilityOptions, visibilityOptionsUpTo, visibilityRank
} from '@/services/visibility'

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

describe('Sichtbarkeit der Amtsadresse', () => {
  it('versteht Zahlen und Namen der Stufen', () => {
    expect(visibilityRank(2)).toBe(2)
    expect(visibilityRank('AllUsers')).toBe(1)
    expect(stricterVisibility('Public', 1)).toBe(1)
    expect(isStricterVisibility(0, 'Public')).toBe(true)
    expect(isStricterVisibility(2, 2)).toBe(false)
  })

  it('deaktiviert Stufen oberhalb der Zuordnung', () => {
    expect(visibilityOptionsUpTo(1).map((option) => option.disabled)).toEqual([false, false, true])
    expect(visibilityOptionsUpTo(null).every((option) => !option.disabled)).toBe(true)
  })

  it('begrenzt eine eigene Auswahl durch eine strengere Zuordnung', () => {
    expect(restrictContextEmailVisibility(0, 2)).toBe(0)
    expect(restrictContextEmailVisibility(2, 1)).toBe(1)
    // Ohne eigene Auswahl folgt die Amtsadresse weiter der Zuordnung
    expect(restrictContextEmailVisibility(1, null)).toBeNull()
    expect(restrictContextEmailVisibility(1, undefined)).toBeNull()
  })

  it('liefert je Zuordnung die wirksame Sichtbarkeit der Amtsadresse', () => {
    expect(effectiveContextEmailVisibilities({ a: 2, b: 1, c: null }, { a: 1, b: null })).toEqual({ a: 1, b: 1 })
  })
})
