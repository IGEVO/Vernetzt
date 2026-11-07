import { describe, expect, it } from 'vitest'
import { detailHeader, isPoster, mediaIcon, mediaTypeLabel, POSTER_ASPECT } from '@/services/eventMedia'
import type { EventMediaDto } from '@/types/api'

describe('Medientypen', () => {
  it('benennt die Typen deutsch', () => {
    expect(mediaTypeLabel(0)).toBe('Titelbild')
    expect(mediaTypeLabel('Cover')).toBe('Titelbild')
    expect(mediaTypeLabel(1)).toBe('Plakat')
    expect(mediaTypeLabel('Poster')).toBe('Plakat')
    expect(mediaTypeLabel(3)).toBe('Bild')
    expect(mediaTypeLabel('Image')).toBe('Bild')
    expect(mediaTypeLabel(2)).toBe('Dokument')
  })

  it('erkennt Plakate', () => {
    expect(isPoster(1)).toBe(true)
    expect(isPoster('Poster')).toBe(true)
    expect(isPoster(3)).toBe(false)
    expect(isPoster(0)).toBe(false)
  })

  it('wählt ein eigenes Symbol für Plakate', () => {
    expect(mediaIcon({ type: 1, contentType: 'image/jpeg' })).toBe('mdi-image-text')
    expect(mediaIcon({ type: 3, contentType: 'image/jpeg' })).toBe('mdi-image')
  })

  it('nutzt das A4-Seitenverhältnis für Plakate', () => {
    expect(POSTER_ASPECT).toBeCloseTo(210 / 297)
  })

  describe('Kopfbild im Detaildialog', () => {
    const media = (id: string, usages = 0) => ({ id, usages }) as EventMediaDto

    it('zeigt das Plakat nicht doppelt als Kopfbild', () => {
      const poster = media('p')
      expect(detailHeader(poster, poster)).toBeNull()
    })

    it('respektiert ein ausdrücklich dem Kopfbild zugewiesenes Plakat', () => {
      const poster = media('p', 2)
      expect(detailHeader(poster, poster)).toBe(poster)
    })

    it('lässt andere Bilder unverändert', () => {
      const header = media('h')
      expect(detailHeader(header, media('p'))).toBe(header)
      expect(detailHeader(header, null)).toBe(header)
      expect(detailHeader(null, media('p'))).toBeNull()
    })
  })
})
