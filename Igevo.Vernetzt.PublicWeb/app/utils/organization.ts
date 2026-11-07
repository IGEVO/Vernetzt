import { entityPath } from './slug'

/**
 * Hierarchy labels for club > department > group. A group never appears with its name alone,
 * because "Bambini" or "1. Mannschaft" is ambiguous without its department and club.
 */
export const PATH_SEPARATOR = ' › '

export function organizationPath(...parts: Array<string | null | undefined>): string {
  return parts.map(part => part?.trim()).filter((part): part is string => !!part).join(PATH_SEPARATOR)
}

/** Organizer of an event with its full path, e.g. "TSV › Handball › Bambini". */
export function eventOrganizerPath(event: { clubName?: string | null; parentDepartmentName?: string | null; departmentName?: string | null }): string {
  return organizationPath(event.clubName, event.parentDepartmentName, event.departmentName)
}

/** First non-empty line of an info text, used as teaser in lists. */
export function firstLine(text: string | null | undefined): string | null {
  return text?.split('\n').map(line => line.trim()).find(Boolean) ?? null
}

/** Higher level (club or department) a value was taken over from. */
export interface InheritedSource { kind?: string | null; id?: string; name?: string | null }

/**
 * Label and link of an inherited value's origin, e.g. "vom Verein" + club page.
 * Returns null for unknown kinds so that nothing misleading is shown.
 */
export function inheritedSourceLink(source: InheritedSource | null | undefined): { prefix: string; name: string; to: string } | null {
  if (!source?.id) return null
  const name = source.name ?? ''
  if (source.kind === 'club') return { prefix: 'vom Verein', name, to: entityPath('vereine', source.id, name) }
  if (source.kind === 'department') return { prefix: 'von der Abteilung', name, to: entityPath('abteilungen', source.id, name) }
  return null
}

/** Public API path of the endpoint that reveals the email of a club or department. */
export function contactDetailsPath(source: InheritedSource): string | null {
  if (!source.id) return null
  if (source.kind === 'club') return `/api/public/v1/clubs/${source.id}/contact-details`
  if (source.kind === 'department') return `/api/public/v1/departments/${source.id}/contact-details`
  return null
}

/** A displayed contact block (address, email, ...) and where its value came from (null = own value). */
export interface InheritedBlock { label: string; source?: InheritedSource | null }

/** "Adresse, Website und Social Media" */
function joinLabels(labels: string[]): string {
  return labels.length < 2 ? labels.join('') : `${labels.slice(0, -1).join(', ')} und ${labels[labels.length - 1]}`
}

/**
 * One line per origin for the footer of the contact box, e.g.
 * "Adresse und Website vom Verein …". If every displayed block comes from the same level,
 * the line simply says "Kontaktdaten vom Verein …".
 */
export function inheritedSummary(blocks: InheritedBlock[]): Array<{ text: string; name: string; to: string }> {
  const groups = new Map<string, { labels: string[]; link: NonNullable<ReturnType<typeof inheritedSourceLink>> }>()
  for (const block of blocks) {
    const link = inheritedSourceLink(block.source)
    if (!link) continue
    const group = groups.get(link.to) ?? { labels: [], link }
    group.labels.push(block.label)
    groups.set(link.to, group)
  }
  const onlyOneSourceForAll = groups.size === 1 && blocks.length > 1 && [...groups.values()][0]!.labels.length === blocks.length
  return [...groups.values()].map(({ labels, link }) => ({
    text: `${onlyOneSourceForAll ? 'Kontaktdaten' : joinLabels(labels)} ${link.prefix}`,
    name: link.name,
    to: link.to
  }))
}
