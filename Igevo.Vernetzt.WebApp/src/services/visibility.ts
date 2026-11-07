import type { VisibilityLevel } from '@/types/api'

/** Visibility choices used by all forms (profile, onboarding, organizations, context e-mails). */
export const visibilityOptions: Array<{ title: string; value: VisibilityLevel }> = [
  { title: 'Nur Administration & Verwaltung', value: 0 },
  { title: 'Alle registrierten Benutzer', value: 1 },
  { title: 'Öffentlich', value: 2 }
]
