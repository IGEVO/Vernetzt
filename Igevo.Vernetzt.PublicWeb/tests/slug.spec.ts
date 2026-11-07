import { describe, expect, it } from 'vitest'
import { entityPath, toSlug } from '../app/utils/slug'

describe('slug URLs', () => {
  it('normalizes German names into stable readable paths', () => {
    expect(toSlug('Schützenverein Weiß-Blau e. V.')).toBe('schutzenverein-weiss-blau-e-v')
    expect(entityPath('vereine', 'abc', 'TSV Übung')).toBe('/vereine/abc/tsv-ubung')
  })

  it('provides a non-empty fallback', () => {
    expect(toSlug('---')).toBe('eintrag')
  })
})
