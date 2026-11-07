import { describe, expect, it } from 'vitest'
import { publicWebLink, toSlug } from '@/services/publicWeb'

describe('publicWebLink', () => {
  const id = '11111111-1111-1111-1111-111111111111'

  it('builds the canonical portal URL', () => {
    expect(publicWebLink('https://portal.example/', 'vereine', id, 'Schützenverein Weiß-Blau e. V.'))
      .toBe(`https://portal.example/vereine/${id}/schutzenverein-weiss-blau-e-v`)
    expect(publicWebLink('https://portal.example', 'abteilungen', id, 'Bambini')).toBe(`https://portal.example/abteilungen/${id}/bambini`)
  })

  it('returns null without a configured portal', () => {
    expect(publicWebLink(null, 'vereine', id, 'TSV')).toBeNull()
    expect(publicWebLink('  ', 'vereine', id, 'TSV')).toBeNull()
  })

  it('falls back to a generic slug', () => {
    expect(toSlug('!!!')).toBe('eintrag')
  })
})
