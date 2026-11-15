import type { VisibilityLevel } from '@/types/api'

/**
 * Visibility choices used by all forms (profile, onboarding, organizations, context e-mails).
 * The subtitle explains who is included; selects show it in the dropdown via `item-props`.
 */
export const visibilityOptions: Array<{ title: string; subtitle: string; value: VisibilityLevel }> = [
  { title: 'Eingeschränkt', subtitle: 'Verwaltung und Konten mit Leserechten', value: 0 },
  { title: 'Alle registrierten Benutzer', subtitle: 'zusätzlich alle angemeldeten Benutzer', value: 1 },
  { title: 'Öffentlich', subtitle: 'zusätzlich alle Besucher der öffentlichen Seiten', value: 2 }
]

/** Display details (label, icon, color) for showing a visibility level as a chip. */
export function visibilityDisplay(value: VisibilityLevel): { title: string; icon: string; color: string } {
  const title = visibilityOptions.find((item) => item.value === value)?.title ?? ''
  switch (value) {
    case 0: return { title, icon: 'mdi-shield-lock-outline', color: 'secondary' }
    case 2: return { title, icon: 'mdi-earth', color: 'warning' }
    default: return { title, icon: 'mdi-account-group-outline', color: 'primary' }
  }
}

const RANKS = { Internal: 0, AllUsers: 1, Public: 2 } as const

/** Numeric level (0 = Internal … 2 = Public), regardless of whether the API sent a number or a name. */
export function visibilityRank(value: VisibilityLevel): 0 | 1 | 2 {
  return typeof value === 'number' ? value : RANKS[value]
}

/** The stricter (less visible) of two levels. */
export function stricterVisibility(a: VisibilityLevel, b: VisibilityLevel): VisibilityLevel {
  return Math.min(visibilityRank(a), visibilityRank(b)) as VisibilityLevel
}

/** Options for a value that may not exceed `max` (e.g. a role address within its assignment); higher levels are disabled. */
export function visibilityOptionsUpTo(max: VisibilityLevel | null): Array<(typeof visibilityOptions)[number] & { disabled: boolean }> {
  return visibilityOptions.map((option) => ({ ...option, disabled: max !== null && visibilityRank(option.value) > visibilityRank(max) }))
}
