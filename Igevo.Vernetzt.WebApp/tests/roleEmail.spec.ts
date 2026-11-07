import { describe, expect, it } from 'vitest'
import { contextEmailError, isRoleEmail, sameEmail } from '@/services/roleEmail'

describe('isRoleEmail', () => {
  it.each([
    'vorstand@verein.de', 'Info@verein.de', 'kontakt@tsv.de', 'kasse@tsv.de', 'geschaeftsstelle@tsv.de',
    'geschäftsstelle@tsv.de', 'jugend-u17@tsv.de', '1.vorsitzender@tsv.de', 'kasse.tennis@tsv.de', 'schriftführer@tsv.de'
  ])('detects the role address %s', (email) => {
    expect(isRoleEmail(email)).toBe(true)
  })

  it.each([
    'max.mustermann@web.de', 'nora.neu@example.test', 'kasseler@web.de', 'informatik.fan@web.de', 'jugendliebe@web.de', '', 'kein-at', null
  ])('accepts the personal address %s', (email) => {
    expect(isRoleEmail(email)).toBe(false)
  })
})

describe('sameEmail', () => {
  it('ignores case and whitespace', () => {
    expect(sameEmail(' Max@Web.de ', 'max@web.de')).toBe(true)
    expect(sameEmail('max@web.de', 'moritz@web.de')).toBe(false)
    expect(sameEmail('', '')).toBe(false)
  })
})

describe('contextEmailError', () => {
  it('allows an empty or distinct role address', () => {
    expect(contextEmailError('', 'max@web.de')).toBeUndefined()
    expect(contextEmailError('vorstand@tsv.de', 'max@web.de')).toBeUndefined()
  })

  it('rejects invalid addresses and the personal address', () => {
    expect(contextEmailError('vorstand', 'max@web.de')).toBeDefined()
    expect(contextEmailError('MAX@web.de', 'max@web.de')).toBeDefined()
  })
})
