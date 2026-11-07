import type { PublicEventImage } from '~/types/public'

/** Smallest preview variant that is at least `minWidth` wide (falls back to the largest one). */
export function smallestVariant(image: PublicEventImage | null | undefined, minWidth: number): string | undefined {
  const variants = [...(image?.variants ?? [])].sort((a, b) => (a.width ?? 0) - (b.width ?? 0))
  return (variants.find(item => (item.width ?? 0) >= minWidth) ?? variants.at(-1))?.url ?? undefined
}

export function srcsetOf(image: PublicEventImage | null | undefined): string | undefined {
  const value = (image?.variants ?? []).map(item => `${item.url} ${item.width}w`).join(', ')
  return value || undefined
}

export function focusPosition(image: PublicEventImage | null | undefined): string {
  return `${(image?.focusX ?? 0.5) * 100}% ${(image?.focusY ?? 0.5) * 100}%`
}
