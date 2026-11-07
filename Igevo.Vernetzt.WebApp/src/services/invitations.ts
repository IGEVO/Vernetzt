import { api, ApiError } from '@/services/api'
import type { InvitationResponse } from '@/types/api'

export interface InvitePayload {
  email: string
  clubId?: string | null
  departmentId?: string | null
  contactPositionId?: string | null
  canEdit?: boolean
  assignmentDetails?: string | null
  /** Optional role / office address for the assignment; becomes its context email after verification. */
  contextEmail?: string | null
  pendingClaims?: number[]
}

/**
 * Sends an invitation. The server answers rejected invitations (missing mandatory settings, existing user, …)
 * with HTTP 200 and `success: false`, so the result must be checked explicitly – otherwise the UI would report
 * a sent invitation although no e-mail was sent.
 */
export async function sendInvitation(payload: InvitePayload): Promise<InvitationResponse> {
  const result = await api.post<InvitationResponse>('/api/invitations/invite', payload)
  if (!result?.success) throw new ApiError(result?.message || 'Die Einladung konnte nicht versendet werden.', 200, [], result?.isExistingUser ? 'EXISTING_USER' : 'INVITATION_REJECTED')
  return result
}
