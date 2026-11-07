export function toSlug(value: string | null | undefined): string {
  return (value ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/ß/g, 'ss')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '') || 'eintrag'
}

export function entityPath(kind: 'vereine' | 'abteilungen' | 'veranstaltungen', id: string, name: string): string {
  return `/${kind}/${id}/${toSlug(name)}`
}
