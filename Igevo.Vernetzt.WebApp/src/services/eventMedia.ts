import type { EventMediaDto, EventMediaType } from '@/types/api'

/** Below this width an image looks blurry in the detail header / public cards. */
export const MIN_RECOMMENDED_WIDTH = 1200

/** Views an image can be explicitly assigned to (bit flags as in the API). */
export const MEDIA_VIEWS = [
  { usage: 1, label: 'Liste', aspect: 1, format: '1:1' },
  { usage: 2, label: 'Kopfbild', aspect: 2, format: '2:1' },
  { usage: 4, label: 'Öffentliche Übersicht', aspect: 3 / 2, format: '3:2' }
] as const

/** A4 portrait (width / height); posters are always shown uncropped in this ratio. */
export const POSTER_ASPECT = 210 / 297

export function isCover(type: EventMediaType): boolean {
  return type === 0 || type === 'Cover'
}

export function isPoster(type: EventMediaType): boolean {
  return type === 1 || type === 'Poster'
}

/**
 * Header image of the detail dialog. The poster has its own uncropped section, so it is only
 * used as header when it was explicitly assigned to the header view (usage flag 2).
 */
export function detailHeader(
  header: EventMediaDto | null | undefined,
  poster: EventMediaDto | null | undefined
): EventMediaDto | null {
  if (!header) return null
  if (poster && header.id === poster.id && (header.usages & 2) === 0) return null
  return header
}

export function hasPreview(media: EventMediaDto | null | undefined): media is EventMediaDto {
  return !!media && (media.variants?.length ?? 0) > 0
}

export function isImageFile(media: EventMediaDto): boolean {
  return media.contentType.startsWith('image/')
}

/** `srcset` attribute from the server generated WebP variants. */
export function srcset(media: EventMediaDto): string {
  return media.variants.map((v) => `${v.url} ${v.width}w`).join(', ')
}

/** Smallest variant that is at least `minWidth` pixels wide (or the largest one). */
export function variantUrl(media: EventMediaDto, minWidth: number): string {
  const variants = media.variants
  if (!variants.length) return media.fileUrl
  return (variants.find((v) => v.width >= minWidth) ?? variants[variants.length - 1]!).url
}

export function objectPosition(focusX: number | undefined, focusY: number | undefined): string {
  const x = Math.round((focusX ?? 0.5) * 100)
  const y = Math.round((focusY ?? 0.5) * 100)
  return `${x}% ${y}%`
}

/** Width / height of the preview, or undefined if unknown. */
export function mediaAspect(media: EventMediaDto): number | undefined {
  return media.width && media.height ? media.width / media.height : undefined
}

export function mediaTypeLabel(type: EventMediaType): string {
  switch (type) {
    case 0: case 'Cover': return 'Titelbild'
    case 1: case 'Poster': return 'Plakat'
    case 3: case 'Image': return 'Bild'
    default: return 'Dokument'
  }
}

export function mediaIcon(media: Pick<EventMediaDto, 'type' | 'contentType'>): string {
  if (isCover(media.type)) return 'mdi-image-frame'
  if (isPoster(media.type)) return 'mdi-image-text'
  if (media.contentType === 'application/pdf') return 'mdi-file-pdf-box'
  if (media.contentType.startsWith('image/')) return 'mdi-image'
  return 'mdi-paperclip'
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

/** Reads the pixel size of a local image file (null if the browser cannot decode it, e.g. HEIC). */
export async function readImageSize(file: File): Promise<{ width: number; height: number } | null> {
  if (!file.type.startsWith('image/')) return null
  try {
    const bitmap = await createImageBitmap(file)
    const size = { width: bitmap.width, height: bitmap.height }
    bitmap.close()
    return size
  } catch {
    return null
  }
}
