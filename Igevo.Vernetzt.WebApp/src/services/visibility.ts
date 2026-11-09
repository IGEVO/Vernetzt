import type { VisibilityLevel } from '@/types/api'

/** Visibility choices used by all forms (profile, onboarding, organizations, context e-mails). */
export const visibilityOptions: Array<{ title: string; value: VisibilityLevel }> = [
  { title: 'Nur Administration & Verwaltung', value: 0 },
  { title: 'Alle registrierten Benutzer', value: 1 },
  { title: 'Öffentlich', value: 2 }
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
