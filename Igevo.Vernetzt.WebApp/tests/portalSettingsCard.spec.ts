import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import PortalSettingsCard from '@/components/admin/PortalSettingsCard.vue'
import type { PortalSettingsDto } from '@/types/api'

const settings: PortalSettingsDto = {
  title: 'Vereine in Musterstadt', subtitle: '', providerName: '', providerUrl: '', noticeText: '', noticeLevel: 'info', hasLogo: false,
  imprint: { mode: 'text', html: '<p>Eigenes Impressum</p>', url: '' },
  privacy: { mode: 'link', html: '', url: 'https://example.org/datenschutz' }
}

vi.mock('@/services/api', () => ({
  ApiError: class extends Error {},
  api: { get: vi.fn(async () => settings), put: vi.fn(), postForm: vi.fn(), delete: vi.fn() }
}))

vi.mock('@/stores/ui', () => ({ useUiStore: () => ({ notify: vi.fn() }) }))

describe('PortalSettingsCard', () => {
  it('shows the form once the settings are loaded', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const wrapper = mount(PortalSettingsCard, {
      global: {
        plugins: [[VueQueryPlugin, { queryClient }]],
        stubs: { RichTextEditor: true },
        // Vuetify is not installed here; its tags render as plain elements.
        config: { warnHandler: () => {} }
      }
    })

    await flushPromises()

    expect(wrapper.find('v-progress-linear').exists()).toBe(false)
    expect(wrapper.find('v-form').exists()).toBe(true)
    expect(wrapper.find('v-text-field').attributes('modelvalue')).toBe('Vereine in Musterstadt')
  })
})
