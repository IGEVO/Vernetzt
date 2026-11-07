import { describe, expect, it } from 'vitest'
import { detectSocialMediaPlatform, socialMediaPlatform, socialMediaPlatforms } from '@/services/socialMedia'

describe('social media platforms', () => {
  it('covers every API enum value exactly once', () => {
    expect(socialMediaPlatforms.map(p => p.value)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8])
    expect(socialMediaPlatforms.every(p => p.path.startsWith('M'))).toBe(true)
  })

  it('looks up platforms by value', () => {
    expect(socialMediaPlatform(1)?.label).toBe('Instagram')
    expect(socialMediaPlatform(null)).toBeUndefined()
    expect(socialMediaPlatform(42)).toBeUndefined()
  })

  it.each([
    ['https://www.instagram.com/tsv', 'Instagram'],
    ['https://m.facebook.com/tsv', 'Facebook'],
    ['https://youtu.be/abc', 'YouTube'],
    ['https://twitter.com/tsv', 'X'],
    ['https://bsky.app/profile/tsv.bsky.social', 'Bluesky'],
    ['https://wa.me/4912345', 'WhatsApp'],
    ['https://www.threads.net/@tsv', 'Threads']
  ])('detects %s as %s', (url, label) => {
    expect(detectSocialMediaPlatform(url)?.label).toBe(label)
  })

  it.each(['https://notfacebook.com/x', 'https://tsv.example', 'kein link', ''])('does not guess a platform for %s', (url) => {
    expect(detectSocialMediaPlatform(url)).toBeUndefined()
  })
})
