import { ref } from 'vue'

export type AvatarOwner = 'users' | 'clubs' | 'departments'

const storageKey = 'avatar_version'

function readVersion(): number {
  try { return Number(localStorage.getItem(storageKey)) || 0 } catch { return 0 }
}

/**
 * Cache-busting version for avatar images. The server caches avatars for an hour, so every avatar URL
 * carries this version; it changes whenever this browser uploads or removes an avatar, so all
 * views (lists, app bar, detail pages) show the new image immediately without a reload.
 */
const version = ref(readVersion())

export function avatarUrl(type: AvatarOwner, id?: string | null): string {
  const owner = type === 'users' && !id ? 'me' : id
  return `/api/avatars/${type}/${owner}?v=${version.value}`
}

export function bumpAvatarVersion(): void {
  version.value = Date.now()
  try { localStorage.setItem(storageKey, String(version.value)) } catch { /* storage unavailable */ }
}

export function initialsOf(...parts: Array<string | null | undefined>): string {
  const words = parts.filter((part): part is string => !!part?.trim())
  if (words.length > 1) return words.slice(0, 2).map((word) => word.trim()[0]).join('').toUpperCase()
  return (words[0] ?? '').trim().slice(0, 2).toUpperCase()
}
