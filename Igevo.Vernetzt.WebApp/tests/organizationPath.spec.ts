import { describe, expect, it } from 'vitest'
import { departmentKind, organizationPath, organizationPathParts } from '@/services/organizationPath'

describe('organizationPath', () => {
  it('joins club, department and group', () => {
    expect(organizationPath({ clubName: 'TSV', parentDepartmentName: 'Handball', departmentName: 'Bambini' }))
      .toBe('TSV › Handball › Bambini')
  })

  it('skips missing levels', () => {
    expect(organizationPath({ clubName: 'TSV', departmentName: 'Handball' })).toBe('TSV › Handball')
    expect(organizationPathParts({ clubName: ' ', departmentName: 'Handball' })).toEqual(['Handball'])
  })

  it('uses the fallback without any level', () => {
    expect(organizationPath({}, 'Ohne Zuordnung')).toBe('Ohne Zuordnung')
  })

  it('names the kind', () => {
    expect(departmentKind(true)).toBe('Gruppe')
    expect(departmentKind(false)).toBe('Abteilung')
  })
})
