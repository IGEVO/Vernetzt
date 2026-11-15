import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrivacyPolicyView from '@/views/PrivacyPolicyView.vue'
import { api } from '@/services/api'

vi.mock('@/services/api', () => ({ api: { get: vi.fn() } }))

const replace = vi.fn()
const originalLocation = window.location

function mountView() {
  return mount(PrivacyPolicyView, {
    global: {
      // Vuetify is not installed here; its tags render as plain elements.
      config: { warnHandler: () => {} }
    }
  })
}

describe('PrivacyPolicyView', () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset()
    replace.mockReset()
    Object.defineProperty(window, 'location', { configurable: true, value: { ...originalLocation, replace } })
  })
  afterEach(() => {
    Object.defineProperty(window, 'location', { configurable: true, value: originalLocation })
  })

  it('shows the own text sanitized', async () => {
    vi.mocked(api.get).mockResolvedValue({ mode: 'text', html: '<h2>Verantwortliche</h2><p>Musterverein</p><img src="x" onerror="alert(1)"><script>alert(1)</script>' })
    const wrapper = mountView()
    await flushPromises()

    expect(api.get).toHaveBeenCalledWith('/api/settings/privacy-policy')
    const text = wrapper.find('.privacy-text')
    expect(text.html()).toContain('<h2>Verantwortliche</h2>')
    expect(text.html()).not.toContain('script')
    expect(text.find('img').exists()).toBe(false)
    expect(replace).not.toHaveBeenCalled()
  })

  it('forwards to the external page in link mode', async () => {
    vi.mocked(api.get).mockResolvedValue({ mode: 'link', url: 'https://example.org/datenschutz' })
    const wrapper = mountView()
    await flushPromises()

    expect(replace).toHaveBeenCalledWith('https://example.org/datenschutz')
    expect(wrapper.find('a[href="https://example.org/datenschutz"]').exists()).toBe(true)
  })

  it('says so when no privacy policy is configured', async () => {
    vi.mocked(api.get).mockResolvedValue({ mode: 'text', html: null })
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Es ist noch keine Datenschutzerklärung hinterlegt.')
  })

  it('shows an error when loading fails', async () => {
    vi.mocked(api.get).mockRejectedValue(new Error('offline'))
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Die Datenschutzerklärung konnte nicht geladen werden.')
  })
})
