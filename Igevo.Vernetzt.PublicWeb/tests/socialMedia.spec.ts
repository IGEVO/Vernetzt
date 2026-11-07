import { describe, expect, it } from 'vitest'
import { socialMediaPlatform, socialMediaPlatforms } from '../app/utils/socialMedia'

describe('social media platforms', () => {
  it('maps every API enum value to a label and icon', () => {
    expect(socialMediaPlatforms.map(p => p.value)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8])
    expect(socialMediaPlatform(2)?.label).toBe('YouTube')
    expect(socialMediaPlatform(99)).toBeUndefined()
  })
})
