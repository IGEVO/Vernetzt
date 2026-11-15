import { ref } from 'vue'

export type AvatarOwner = 'users' | 'clubs' | 'departments'

/**
 * Cache-busting version for avatar images, kept in memory only. The server answers avatars with
 * "private, no-cache" and an ETag, so after a reload the browser revalidates and shows the current image
 * anyway; this version only makes an upload or removal in this tab visible in all views at once.
 */
const version = ref(0)

export function avatarUrl(type: AvatarOwner, id?: string | null): string {
  const owner = type === 'users' && !id ? 'me' : id
  return version.value ? `/api/avatars/${type}/${owner}?v=${version.value}` : `/api/avatars/${type}/${owner}`
}

export function bumpAvatarVersion(): void {
  version.value = Date.now()
}

export function initialsOf(...parts: Array<string | null | undefined>): string {
  const words = parts.filter((part): part is string => !!part?.trim())
  if (words.length > 1) return words.slice(0, 2).map((word) => word.trim()[0]).join('').toUpperCase()
  return (words[0] ?? '').trim().slice(0, 2).toUpperCase()
}
