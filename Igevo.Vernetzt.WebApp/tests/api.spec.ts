import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest, onAuthenticationFailure, onPasskeyRequired, toQuery } from '@/services/api'

describe('API Client', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('baut Querystrings ohne leere Werte', () => {
    expect(toQuery({ page: 2, q: 'Turnen', empty: '', nil: null })).toBe('?page=2&q=Turnen')
  })

  it('meldet ein 401 an den Auth-Handler, ohne einen Token-Refresh zu versuchen', async () => {
    const calls: string[] = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      calls.push(String(input))
      return Response.json({ success: false, message: 'Nicht angemeldet' }, { status: 401 })
    }))
    const handler = vi.fn()
    onAuthenticationFailure(handler)

    await expect(apiRequest('/api/users/me')).rejects.toMatchObject({ name: 'ApiError', status: 401 })
    expect(calls).toEqual(['/api/users/me'])
    expect(handler).toHaveBeenCalledTimes(1)
  })

  it('löst bei 401 von Auth-Endpunkten keinen Auth-Handler aus', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ success: false, message: 'Link ungültig' }, { status: 401 })))
    const handler = vi.fn()
    onAuthenticationFailure(handler)

    await expect(apiRequest('/api/auth/login-link/verify', { method: 'POST' })).rejects.toMatchObject({ status: 401, message: 'Link ungültig' })
    expect(handler).not.toHaveBeenCalled()
  })

  it('meldet ein 403 mit PASSKEY_REQUIRED an den Passkey-Handler und übernimmt Code und Meldung', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ success: false, error: { code: 'PASSKEY_REQUIRED', message: 'Bitte mit Passkey anmelden.' } }, { status: 403 })))
    const handler = vi.fn()
    onPasskeyRequired(handler)

    await expect(apiRequest('/api/users')).rejects.toMatchObject({ status: 403, code: 'PASSKEY_REQUIRED', message: 'Bitte mit Passkey anmelden.' })
    expect(handler).toHaveBeenCalledTimes(1)
  })

  it('löst bei anderen 403-Fehlern keinen Passkey-Handler aus', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ success: false, error: { code: 'FORBIDDEN', message: 'Keine Berechtigung.' } }, { status: 403 })))
    const handler = vi.fn()
    onPasskeyRequired(handler)

    await expect(apiRequest('/api/users')).rejects.toMatchObject({ status: 403, code: 'FORBIDDEN' })
    expect(handler).not.toHaveBeenCalled()
  })
})
