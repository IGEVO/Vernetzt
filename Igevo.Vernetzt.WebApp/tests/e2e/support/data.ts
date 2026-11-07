import type {
  CalendarFeedTokenDto, ClubDto, ClubSummaryDto, ContactAssignmentDto, ContactDto, DepartmentDto, DepartmentSummaryDto,
  EventDto, MyContactAssignmentDto, NamedEntityDto, PagedResult, RecipientCandidateDto, UserDto
} from '../../../src/types/api'

/** Test data factories: sensible defaults, override only what a test cares about. */

export const ids = {
  admin: '00000000-0000-0000-0000-0000000000a1',
  user: '00000000-0000-0000-0000-0000000000b1',
  club: '00000000-0000-0000-0000-0000000000c1',
  club2: '00000000-0000-0000-0000-0000000000c2',
  department: '00000000-0000-0000-0000-0000000000d1',
  group: '00000000-0000-0000-0000-0000000000e1'
}

export const clubTypes: NamedEntityDto[] = [
  { id: 'ct-sport', name: 'Sportverein', isActive: true },
  { id: 'ct-music', name: 'Musikverein', isActive: true }
]
export const positions: NamedEntityDto[] = [
  { id: 'pos-chair', name: 'Vorsitz', isActive: true },
  { id: 'pos-treasurer', name: 'Kasse', isActive: true }
]

export function user(overrides: Partial<UserDto> = {}): UserDto {
  return {
    id: ids.user, email: 'erika@example.test', firstName: 'Erika', lastName: 'Muster', phoneNumber: null,
    emailVisibility: 1, phoneVisibility: 1, emailVerified: true, hasAvatar: false, claims: [],
    createdAt: '2026-10-01T08:00:00Z', ...overrides
  }
}
export const admin = (overrides: Partial<UserDto> = {}) => user({ id: ids.admin, email: 'ada@example.test', firstName: 'Ada', lastName: 'Admin', claims: [0], ...overrides })
export const manager = (overrides: Partial<UserDto> = {}) => user({ id: ids.admin, email: 'mo@example.test', firstName: 'Mo', lastName: 'Manager', claims: [1], ...overrides })

export function departmentSummary(overrides: Partial<DepartmentSummaryDto> = {}): DepartmentSummaryDto {
  return { id: ids.department, name: 'Fußball', city: 'Musterstadt', email: null, contactPersonsCount: 1, canEdit: true, hasAvatar: false, ...overrides }
}
export function clubSummary(overrides: Partial<ClubSummaryDto> = {}): ClubSummaryDto {
  return { id: ids.club, name: 'TSV Musterstadt', clubTypeId: 'ct-sport', clubTypeName: 'Sportverein', city: 'Musterstadt', email: 'info@tsv.test', websiteUrl: 'https://tsv.test', hasAvatar: false, departmentCount: 1, matchingDepartments: [], ...overrides }
}
export function assignment(overrides: Partial<ContactAssignmentDto> = {}): ContactAssignmentDto {
  return { assignmentId: 'as-1', userId: 'u-max', firstName: 'Max', lastName: 'Beispiel', email: 'max@example.test', phoneNumber: '0151 123', contactPositionId: 'pos-chair', positionName: 'Vorsitz', canEdit: false, assignmentDetails: null, hasAvatar: false, ...overrides }
}
export function club(overrides: Partial<ClubDto> = {}): ClubDto {
  return {
    ...clubSummary(), street: 'Hauptstraße 1', postalCode: '12345', contactPersonsVisibility: 1,
    contactAssignments: [assignment()], departments: [departmentSummary()], canEdit: true, canDelete: true, canManageContacts: true,
    ...overrides
  }
}
export function department(overrides: Partial<DepartmentDto> = {}): DepartmentDto {
  return {
    ...departmentSummary(), clubId: ids.club, clubName: 'TSV Musterstadt', street: null, postalCode: null, website: null,
    contactPersonsVisibility: 1, contactAssignments: [], canDelete: true, canManageContacts: true,
    isGroup: false, parentDepartmentId: null, parentDepartmentName: null, groups: [], ...overrides
  }
}
export function contact(overrides: Partial<ContactDto> = {}): ContactDto {
  return {
    id: 'u-max', firstName: 'Max', lastName: 'Beispiel', email: 'max@example.test', phoneNumber: '0151 123', hasAvatar: false,
    organizations: [{ organizationId: ids.club, organizationName: 'TSV Musterstadt', type: 0, positionName: 'Vorsitz' }],
    ...overrides
  }
}
export function myAssignment(overrides: Partial<MyContactAssignmentDto> = {}): MyContactAssignmentDto {
  return { assignmentId: 'my-1', organizationId: ids.club, organizationName: 'TSV Musterstadt', organizationType: 0, positionName: 'Vorsitz', contextEmail: null, contextEmailVerified: false, contextEmailVisibility: 1, ...overrides }
}
export function recipient(overrides: Partial<RecipientCandidateDto> = {}): RecipientCandidateDto {
  return {
    assignmentId: 'as-1', isAssignmentRecipient: true, userId: 'u-max', firstName: 'Max', lastName: 'Beispiel', displayEmail: 'max@example.test',
    organizationId: ids.club, organizationName: 'TSV Musterstadt', organizationType: 0, parentClubId: null, parentClubName: null,
    clubTypeId: 'ct-sport', positionId: 'pos-chair', positionName: 'Vorsitz', emailBadgeLabel: '', hasAvatar: false, ...overrides
  }
}
export function event(overrides: Partial<EventDto> = {}): EventDto {
  const start = new Date(); start.setDate(start.getDate() + 3); start.setHours(18, 0, 0, 0)
  const end = new Date(start); end.setHours(20)
  return {
    id: 'ev-1', title: 'Jahreshauptversammlung', description: '<p>Alle Mitglieder sind eingeladen.</p>', location: 'Vereinsheim',
    startUtc: start.toISOString(), endUtc: end.toISOString(), isAllDay: false, isHighlight: false,
    clubId: ids.club, clubName: 'TSV Musterstadt', canEdit: true, media: [], ...overrides
  }
}
export function feedToken(overrides: Partial<CalendarFeedTokenDto> = {}): CalendarFeedTokenDto {
  return { id: 'feed-1', scope: 2, includeDepartments: true, highlightsOnly: false, isRevoked: false, createdAt: '2026-11-01T08:00:00Z', updatedAt: '2026-11-01T08:00:00Z', feedUrl: 'http://localhost:5173/api/calendar-feeds/abc123.ics', ...overrides }
}
export function paged<T>(items: T[], page = 1, pageSize = 12): PagedResult<T> {
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize))
  return { items, totalCount: items.length, page, pageSize, totalPages, hasPreviousPage: page > 1, hasNextPage: page < totalPages }
}
