/**
 * Hierarchy labels for club > department > group.
 *
 * A group must never appear with its name alone ("Bambini" is ambiguous), so every list that shows
 * an organization uses these helpers.
 */

export const PATH_SEPARATOR = ' › '

export interface OrganizationPathParts {
  clubName?: string | null
  /** For groups: the parent department */
  parentDepartmentName?: string | null
  /** Department or group name */
  departmentName?: string | null
}

/** All known levels from the top, e.g. ["TSV", "Handball", "Bambini"]. Empty parts are skipped. */
export function organizationPathParts(parts: OrganizationPathParts): string[] {
  return [parts.clubName, parts.parentDepartmentName, parts.departmentName]
    .map((part) => part?.trim())
    .filter((part): part is string => !!part)
}

/** Compact label, e.g. "TSV › Handball › Bambini". */
export function organizationPath(parts: OrganizationPathParts, fallback = ''): string {
  const path = organizationPathParts(parts)
  return path.length ? path.join(PATH_SEPARATOR) : fallback
}

/** Kind label for a department-like entry. */
export function departmentKind(isGroup: boolean | undefined | null): 'Gruppe' | 'Abteilung' {
  return isGroup ? 'Gruppe' : 'Abteilung'
}

/** Organizer of an event with its full path, e.g. "TSV › Handball › Bambini". */
export function eventOrganizer(item: { clubName?: string | null; departmentName?: string | null; parentDepartmentName?: string | null }): string {
  return organizationPath({ clubName: item.clubName, parentDepartmentName: item.parentDepartmentName, departmentName: item.departmentName }, 'Ohne Zuordnung')
}
