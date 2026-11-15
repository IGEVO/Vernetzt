import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { START_LOCATION, type RouteLocationNormalized } from 'vue-router'
import router, { isSamePage } from '@/router'


const location = (path: string, query: Record<string, string> = {}) => ({ path, query }) as unknown as RouteLocationNormalized

describe('router', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('treats query-only changes as the same page', () => {
    expect(isSamePage(location('/clubs', { q: 'tsv' }), location('/clubs'))).toBe(true)
    expect(isSamePage(location('/contacts'), location('/clubs'))).toBe(false)
    // The very first navigation is a page change, even to the same path
    expect(isSamePage(location('/'), START_LOCATION)).toBe(false)
  })

  it('keeps scroll position on query-only changes and scrolls to the top on page changes', async () => {
    const scrollBehavior = router.options.scrollBehavior!
    expect(await scrollBehavior(location('/clubs', { q: 'tsv' }), location('/clubs'), null)).toBe(false)
    expect(await scrollBehavior(location('/contacts'), location('/clubs'), null)).toEqual({ top: 0 })
  })

  it('moves the focus to the heading only when the page changes', async () => {
    const frame = vi.spyOn(window, 'requestAnimationFrame').mockImplementation(() => 0)
    await router.push('/login')
    expect(frame).toHaveBeenCalledTimes(1)

    await router.replace({ path: '/login', query: { q: 'tsv' } })
    expect(frame).toHaveBeenCalledTimes(1)
    expect(document.title).toContain('Vernetzt')
    frame.mockRestore()
  })
})
