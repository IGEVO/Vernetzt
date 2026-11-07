import { describe, expect, it } from 'vitest'
import { contactDetailsPath, eventOrganizerPath, firstLine, inheritedSourceLink, inheritedSummary, organizationPath } from '../app/utils/organization'

describe('organization hierarchy', () => {
  it('joins club, department and group and skips missing levels', () => {
    expect(organizationPath('TSV', 'Handball', 'Bambini')).toBe('TSV › Handball › Bambini')
    expect(organizationPath('TSV', null, ' ')).toBe('TSV')
  })

  it('builds the event organizer path', () => {
    expect(eventOrganizerPath({ clubName: 'TSV', parentDepartmentName: 'Handball', departmentName: 'Bambini' }))
      .toBe('TSV › Handball › Bambini')
    expect(eventOrganizerPath({ clubName: 'TSV' })).toBe('TSV')
  })

  it('takes the first non-empty line as teaser', () => {
    expect(firstLine('\n  Training freitags\nHalle Nord')).toBe('Training freitags')
    expect(firstLine(null)).toBeNull()
  })
})

describe('inherited values', () => {
  const clubId = '11111111-1111-1111-1111-111111111111'
  const departmentId = '22222222-2222-2222-2222-222222222222'

  it('names and links the club or department a value comes from', () => {
    expect(inheritedSourceLink({ kind: 'club', id: clubId, name: 'TSV Müßingen' }))
      .toEqual({ prefix: 'vom Verein', name: 'TSV Müßingen', to: `/vereine/${clubId}/tsv-mussingen` })
    expect(inheritedSourceLink({ kind: 'department', id: departmentId, name: 'Handball' }))
      .toEqual({ prefix: 'von der Abteilung', name: 'Handball', to: `/abteilungen/${departmentId}/handball` })
  })

  it('shows nothing for own values or unknown kinds', () => {
    expect(inheritedSourceLink(null)).toBeNull()
    expect(inheritedSourceLink({ kind: 'other', id: clubId, name: 'X' })).toBeNull()
  })

  it('reveals an inherited email through the endpoint of its source', () => {
    expect(contactDetailsPath({ kind: 'club', id: clubId })).toBe(`/api/public/v1/clubs/${clubId}/contact-details`)
    expect(contactDetailsPath({ kind: 'department', id: departmentId })).toBe(`/api/public/v1/departments/${departmentId}/contact-details`)
  })
})

describe('inherited summary', () => {
  const club = { kind: 'club', id: '11111111-1111-1111-1111-111111111111', name: 'FC Beispielberg 09' }
  const department = { kind: 'department', id: '22222222-2222-2222-2222-222222222222', name: 'Junioren' }

  it('groups blocks by their origin', () => {
    const lines = inheritedSummary([
      { label: 'Adresse', source: club },
      { label: 'E-Mail', source: department },
      { label: 'Website', source: club },
      { label: 'Social Media', source: club }
    ])
    expect(lines.map(line => `${line.text} ${line.name}`)).toEqual([
      'Adresse, Website und Social Media vom Verein FC Beispielberg 09',
      'E-Mail von der Abteilung Junioren'
    ])
  })

  it('says "Kontaktdaten" when everything comes from one level', () => {
    const lines = inheritedSummary([{ label: 'Adresse', source: club }, { label: 'Website', source: club }])
    expect(lines.map(line => line.text)).toEqual(['Kontaktdaten vom Verein'])
  })

  it('skips own values and is empty without inherited ones', () => {
    expect(inheritedSummary([{ label: 'Adresse', source: null }, { label: 'Website', source: club }]).map(line => line.text))
      .toEqual(['Website vom Verein'])
    expect(inheritedSummary([{ label: 'Adresse' }])).toEqual([])
  })
})
