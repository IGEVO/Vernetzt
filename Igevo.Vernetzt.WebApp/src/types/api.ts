export interface ApiResponse<T> {
  success: boolean
  data: T | null
  message?: string | null
  errors?: string[] | null
  error?: { code: string; message: string; details?: string[] | null } | null
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

export type UserClaim = 'SystemAdmin' | 'Manager' | 'DataReader' | 0 | 1 | 2
export type VisibilityLevel = 'Internal' | 'AllUsers' | 'Public' | 0 | 1 | 2
export type EventMediaType = 'Cover' | 'Poster' | 'Attachment' | 'Image' | 0 | 1 | 2 | 3

export interface UserDto {
  id: string
  email: string
  firstName: string
  lastName: string
  fullName?: string
  phoneNumber?: string | null
  emailVisibility: VisibilityLevel
  phoneVisibility: VisibilityLevel
  emailVerified: boolean
  hasAvatar: boolean
  /** Number of registered passkeys (only in the user administration list) */
  passkeyCount?: number | null
  claims: UserClaim[]
  createdAt: string
  updatedAt?: string | null
  lastLoginAt?: string | null
}

export interface ContactAssignmentDto {
  assignmentId: string
  userId: string
  firstName?: string | null
  lastName?: string | null
  email?: string | null
  phoneNumber?: string | null
  contactPositionId?: string | null
  positionName?: string | null
  canEdit: boolean
  assignmentDetails?: string | null
  contextEmail?: string | null
  contextEmailVerified?: boolean
  /** Who may see the assignment and its role/office address (chosen by the assigned user) */
  visibility?: VisibilityLevel
  /** False while the assigned user has not consented yet (no rights, only visible internally) */
  isConfirmed?: boolean
  hasAvatar?: boolean
}

/** 0 Facebook, 1 Instagram, 2 YouTube, 3 TikTok, 4 X, 5 Mastodon, 6 Bluesky, 7 Threads, 8 WhatsApp */
export type SocialMediaPlatform = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8

export interface SocialMediaLinkDto {
  platform: SocialMediaPlatform
  url: string
}

/** Editable form row for a social media link (platform may still be unset). */
export interface SocialMediaLinkRow {
  key: number
  platform: SocialMediaPlatform | null
  url: string
}

export interface ClubSummaryDto {
  id: string
  name: string
  /** Optional abbreviation, e.g. "TVM" */
  shortName?: string | null
  clubTypeId?: string | null
  clubTypeName?: string | null
  city?: string | null
  email?: string | null
  websiteUrl?: string | null
  hasAvatar: boolean
  departmentCount: number
  /** Departments and groups matching the search term (only filled while searching) */
  matchingDepartments?: Array<{ id: string; name: string; parentDepartmentName?: string | null }> | null
}

export interface ClubDto extends ClubSummaryDto {
  /** Public info text (plain text, line breaks preserved) */
  description?: string | null
  street?: string | null
  postalCode?: string | null
  socialMediaLinks?: SocialMediaLinkDto[] | null
  contactAssignments: ContactAssignmentDto[]
  departments: DepartmentSummaryDto[]
  canEdit: boolean
  canDelete: boolean
  canManageContacts: boolean
}

export interface DepartmentSummaryDto {
  id: string
  name: string
  /** Public info text, e.g. training times (plain text) */
  description?: string | null
  /** Set for groups: the department this group belongs to */
  parentDepartmentId?: string | null
  /** Number of groups below a department */
  groupCount?: number
  city?: string | null
  email?: string | null
  contactPersonsCount: number
  canEdit: boolean
  hasAvatar: boolean
}

export interface DepartmentDto extends DepartmentSummaryDto {
  /** Optional abbreviation, e.g. "FB" */
  shortName?: string | null
  clubId: string
  clubName?: string | null
  parentDepartmentName?: string | null
  /** True for groups (one level below a department) */
  isGroup: boolean
  /** Groups below this department (always empty for groups) */
  groups: DepartmentSummaryDto[]
  street?: string | null
  postalCode?: string | null
  website?: string | null
  socialMediaLinks?: SocialMediaLinkDto[] | null
  contactAssignments: ContactAssignmentDto[]
  canDelete: boolean
  canManageContacts: boolean
}

export interface ContactOrganizationDto {
  organizationId: string
  organizationName: string
  /** 0 = Club, 1 = Department */
  type: 'Club' | 'Department' | 'None' | 0 | 1 | 2
  /** Parent club for departments and groups */
  parentId?: string | null
  parentName?: string | null
  /** Parent department for groups */
  parentDepartmentId?: string | null
  parentDepartmentName?: string | null
  positionId?: string | null
  positionName?: string | null
  assignmentEmail?: string | null
  /** False while the user has not consented to the assignment yet */
  isConfirmed?: boolean
}

export interface ContactDto {
  /** User id */
  id: string
  firstName: string
  lastName: string
  fullName?: string
  email?: string | null
  phoneNumber?: string | null
  hasAvatar?: boolean
  organizations: ContactOrganizationDto[]
}

export interface EventMediaDto {
  id: string
  type: EventMediaType
  fileName: string
  contentType: string
  fileSize: number
  sortOrder: number
  fileUrl: string
  /** Focal point 0..1, used as object-position when cropping into other aspect ratios */
  focusX: number
  focusY: number
  /** Explicit view assignments as bit flags: 1 = list, 2 = header, 4 = public card */
  usages: number
  /** Preview dimensions; null if no preview exists */
  width?: number | null
  height?: number | null
  /** Responsive WebP previews, ascending by width; empty for documents without preview */
  variants: EventMediaVariantDto[]
}

export interface EventMediaVariantDto {
  width: number
  url: string
}

export interface EventDto {
  id: string
  title: string
  description?: string | null
  location?: string | null
  startUtc: string
  endUtc: string
  isAllDay: boolean
  isHighlight: boolean
  clubId?: string | null
  departmentId?: string | null
  clubName?: string | null
  departmentName?: string | null
  /** Set if the event belongs to a group */
  parentDepartmentName?: string | null
  canEdit: boolean
  media: EventMediaDto[]
  /** Resolved display image (cover → image → any media with preview) */
  image?: EventMediaDto | null
  /** Per-view images (explicit assignment, otherwise `image`) */
  listImage?: EventMediaDto | null
  headerImage?: EventMediaDto | null
  cardImage?: EventMediaDto | null
  /** Poster (usually A4 portrait), shown uncropped; no fallback */
  posterImage?: EventMediaDto | null
}

export interface DashboardDataDto {
  userDisplayName: string
  myClubs: Array<{
    id: string
    name: string
    city?: string | null
    isContactPerson: boolean
    canEdit: boolean
    departments: Array<{ id: string; name: string; parentDepartmentName?: string | null; isContactPerson: boolean; canEdit: boolean }>
  }>  /** Own assignments still waiting for consent */
  pendingAssignmentCount?: number
}

export interface NamedEntityDto { id: string; name: string; description?: string | null; isActive?: boolean }

export type AuditAction = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9

export interface AuditLogDto {
  id: string
  entityType: string
  entityId: string
  entityDisplayName?: string | null
  action: AuditAction
  userId: string
  userName?: string | null
  timestamp: string
  ipAddress?: string | null
  oldValues?: string | null
  newValues?: string | null
  notes?: string | null
  resolvedOldValues?: string | null
  resolvedNewValues?: string | null
  resolvedNotes?: string | null
}

export interface InvitationResponse { success: boolean; message: string; isExistingUser: boolean }

export interface CalendarFeedTokenDto { id: string; scope: string | number; clubId?: string | null; departmentId?: string | null; includeDepartments: boolean; highlightsOnly: boolean; isRevoked: boolean; createdAt: string; updatedAt: string; feedUrl?: string | null }
export interface CalendarFeedTokenCreatedDto { token: CalendarFeedTokenDto; feedUrl: string }
export interface EmailRelayTokenDto { id: string; emailAddress: string; createdAt: string; expiresAt: string; recipientCount: number }
export interface RecipientCandidateDto {
  assignmentId: string
  isAssignmentRecipient: boolean
  userId: string
  firstName: string
  lastName: string
  fullName?: string
  displayEmail?: string | null
  organizationId: string
  organizationName: string
  organizationType: string | number
  parentClubId?: string | null
  parentClubName?: string | null
  parentDepartmentId?: string | null
  parentDepartmentName?: string | null
  clubTypeId?: string | null
  positionId?: string | null
  positionName?: string | null
  assignmentDetails?: string | null
  emailBadgeLabel?: string
  hasAvatar?: boolean
}
export interface PasskeyInfo { credentialId: string; name?: string | null; createdAt: string; isBackedUp?: boolean }
export interface MyContactAssignmentDto {
  assignmentId: string; organizationId: string; organizationName: string; organizationType: string | number
  parentClubName?: string | null; parentDepartmentName?: string | null; positionName?: string | null; assignmentDetails?: string | null
  contextEmail?: string | null; contextEmailVerified: boolean; visibility: VisibilityLevel; isConfirmed: boolean; confirmedAt?: string | null
  effectiveEmailBadgeLabel?: string
}
export interface InvitationDetails {
  email: string; expiresAt: string; privacyPolicyUrl: string; passkeyRequired?: boolean
  pendingAssignments: Array<{ invitationId: string; clubName?: string | null; departmentName?: string | null; parentDepartmentName?: string | null; positionName?: string | null; contextEmail?: string | null }>
}
export interface VersionInfo { version: string; gitHash?: string; userDisplayVersion?: string }

export type BackupTrigger = 'Scheduled' | 'Manual' | 'PreMigration' | 0 | 1 | 2

export interface BackupFileDto {
  fileName: string
  createdUtc: string
  sizeBytes: number
  trigger: BackupTrigger
}

export interface BackupStatusDto {
  scheduleEnabled: boolean
  scheduleTime: string
  timeZone: string
  nextRunUtc?: string | null
  keepDaily: number
  keepWeekly: number
  beforeMigration: boolean
  directory: string
  lastFailureUtc?: string | null
  lastError?: string | null
  backups: BackupFileDto[]
}

export interface AppSettingsDto {
  contactName?: string | null
  contactEmail?: string | null
  privacyPolicyUrl?: string | null
  [key: string]: unknown
}

export interface SupportContactDto {
  name: string
  email: string
}

export type PortalLegalPageMode = 'text' | 'link'

export interface PortalLegalPageDto {
  mode: PortalLegalPageMode
  html: string
  url: string
}

export interface PortalSettingsDto {
  title: string
  subtitle: string
  imprint: PortalLegalPageDto
  privacy: PortalLegalPageDto
  providerName: string
  providerUrl: string
  noticeText: string
  noticeLevel: 'info' | 'warning'
  hasLogo: boolean
}

/** School holiday or public holiday (central calendar layer, never part of iCal exports). Dates are yyyy-MM-dd, end inclusive. */
export interface HolidayEntryDto {
  id: string
  title: string
  startDate: string
  endDate: string
  /** Subscription the entry was imported from; null for manual entries */
  sourceId?: string | null
  sourceName?: string | null
}

export interface SaveHolidayEntryDto {
  title: string
  startDate: string
  endDate: string
}

export interface HolidaySourceDto {
  id: string
  name: string
  url: string
  isEnabled: boolean
  lastSyncedAt?: string | null
  lastSyncError?: string | null
  entryCount: number
}

export interface SaveHolidaySourceDto {
  name: string
  url: string
  isEnabled: boolean
}
export interface CardDavStatusDto {
  hasAccess: boolean; createdAt?: string | null; lastUsedAt?: string | null
  serverUrl: string; username: string; includeOrganizations: boolean
}
export interface CardDavAccessCreatedDto { password: string; status: CardDavStatusDto }
