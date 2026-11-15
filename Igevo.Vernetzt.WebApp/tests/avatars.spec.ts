import { describe, expect, it } from 'vitest'
import { avatarUrl, bumpAvatarVersion, initialsOf } from '@/services/avatars'

describe('avatars', () => {
  it('builds URLs without version, then cache-busts after a change, without using local storage', () => {
    expect(avatarUrl('users')).toBe('/api/avatars/users/me')
    expect(avatarUrl('clubs', 'club-1')).toBe('/api/avatars/clubs/club-1')

    bumpAvatarVersion()

    expect(avatarUrl('clubs', 'club-1')).toMatch(/^\/api\/avatars\/clubs\/club-1\?v=\d+$/)
    expect(localStorage.length).toBe(0)
  })

  it('derives initials', () => {
    expect(initialsOf('Erika', 'Mustermann')).toBe('EM')
    expect(initialsOf('Turnverein', null)).toBe('TU')
    expect(initialsOf(undefined, ' ')).toBe('')
  })
})
