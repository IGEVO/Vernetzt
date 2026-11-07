import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api, onAuthenticationFailure, onPasskeyRequired } from '@/services/api'
import { queryClient } from '@/plugins/queryClient'
import type { UserClaim, UserDto } from '@/types/api'

interface AuthStatus { isAuthenticated: boolean; userId?: string | null; passkeyRequired?: boolean; hasPasskey?: boolean; publicWebUrl?: string | null }

export const PASSKEY_REQUIRED_PATH = '/passkey-erforderlich'

const roleNames: Record<number, string> = { 0: 'SystemAdmin', 1: 'Manager', 2: 'DataReader' }

export const useAuthStore = defineStore('auth', () => {
  const user = ref<UserDto | null>(null)
  const initialized = ref(false)
  const loading = ref(false)
  /** Privileged role, but the session was started with a login link: only passkey setup / sign-in is possible. */
  const passkeyRequired = ref(false)
  const hasPasskey = ref(false)
  /** Base URL of the public portal for "public page" links; null if not configured. */
  const publicWebUrl = ref<string | null>(null)
  const isAuthenticated = computed(() => !!user.value)

  function roleName(role: UserClaim): string { return typeof role === 'number' ? roleNames[role] ?? String(role) : role }
  function hasRole(...roles: string[]): boolean { return user.value?.claims.some((claim) => roles.includes(roleName(claim))) ?? false }
  /** Drops the session state and all cached API data, so nothing of this user is shown to the next one. */
  function clear(): void { user.value = null; passkeyRequired.value = false; hasPasskey.value = false; queryClient.clear() }
  async function initialize(force = false): Promise<void> {
    if (initialized.value && !force) return
    loading.value = true
    try {
      const status = await api.get<AuthStatus>('/api/auth/status')
      passkeyRequired.value = status.passkeyRequired ?? false
      hasPasskey.value = status.hasPasskey ?? false
      publicWebUrl.value = status.publicWebUrl ?? null
      user.value = status.isAuthenticated ? await api.get<UserDto>('/api/users/me') : null
    } catch { clear() }
    finally { initialized.value = true; loading.value = false }
  }
  async function requestLoginLink(email: string, returnUrl?: string): Promise<void> {
    await api.post<void>('/api/auth/login-link/request', { email, returnUrl })
  }
  async function verifyLoginLink(userId: string, token: string): Promise<void> {
    await api.post<void>('/api/auth/login-link/verify', { userId, token })
    queryClient.clear()
    await initialize(true)
  }
  async function signInWithPasskey(credentialJson: string): Promise<void> {
    await api.post<void>('/api/auth/passkeys/signin', { credentialJson })
    queryClient.clear()
    await initialize(true)
  }
  async function logout(): Promise<void> {
    try { await api.post<void>('/api/auth/logout') } finally { clear() }
  }
  async function logoutEverywhere(): Promise<void> {
    try { await api.post<void>('/api/auth/logout-all') } finally { clear() }
  }

  onPasskeyRequired(() => {
    passkeyRequired.value = true
    if (location.pathname !== PASSKEY_REQUIRED_PATH) {
      const returnUrl = `${location.pathname}${location.search}${location.hash}`
      location.assign(`${PASSKEY_REQUIRED_PATH}?returnUrl=${encodeURIComponent(returnUrl)}`)
    }
  })
  onAuthenticationFailure(() => {
    clear()
    if (location.pathname !== '/login') {
      const returnUrl = `${location.pathname}${location.search}${location.hash}`
      location.assign(`/login?returnUrl=${encodeURIComponent(returnUrl)}`)
    }
  })
  return { user, initialized, loading, isAuthenticated, passkeyRequired, hasPasskey, publicWebUrl, hasRole, clear, initialize, requestLoginLink, verifyLoginLink, signInWithPasskey, logout, logoutEverywhere }
})
