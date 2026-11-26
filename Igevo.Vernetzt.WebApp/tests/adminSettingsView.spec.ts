import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import AdminSettingsView from '@/views/admin/AdminSettingsView.vue'
import { api } from '@/services/api'

vi.mock('@/services/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/api')>()),
  api: {
    get: vi.fn(async (url: string) => url === '/api/settings'
      ? { contactName: 'Ada', contactEmail: 'ada@example.test', privacyPolicyMode: 'link', privacyPolicyUrl: '', privacyPolicyHtml: '', imprintMode: 'link', imprintUrl: '', imprintHtml: '' }
      : url === '/api/backups' ? { backups: [] } : []),
    put: vi.fn(), post: vi.fn()
  }
}))
vi.mock('@/stores/ui', () => ({ useUiStore: () => ({ notify: vi.fn() }) }))

// Vuetify is not installed here: stand-ins for the inputs so v-model can be driven
const Input = { name: 'Input', props: ['modelValue'], emits: ['update:modelValue'], template: '<div><slot /></div>' }

describe('AdminSettingsView', () => {
  it('saves imprint and privacy policy together', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const wrapper = mount(AdminSettingsView, {
      global: {
        plugins: [[VueQueryPlugin, { queryClient }]],
        stubs: { 'v-text-field': Input, 'v-btn-toggle': Input, 'v-tabs': Input, 'v-window': Input, RichTextEditor: Input, PortalSettingsCard: true, CalendarFeedLink: true, PageHeader: true },
        config: { warnHandler: () => {} }
      }
    })
    await flushPromises()

    const inputs = () => wrapper.findAllComponents(Input)
    const field = (label: string) => inputs().find((input) => input.attributes('label') === label)!
    // Tabs and window both hold the selected tab
    const tabs = inputs().filter((input) => input.props('modelValue') === 'privacy')
    expect(tabs).toHaveLength(2)
    for (const tab of tabs) tab.vm.$emit('update:modelValue', 'imprint')
    field('E-Mail-Adresse *').vm.$emit('update:modelValue', 'info@example.test')
    field('URL der Datenschutzerklärung *').vm.$emit('update:modelValue', 'https://example.test/datenschutz')
    field('URL des Impressums').vm.$emit('update:modelValue', 'https://example.test/alt')
    // One mode toggle per tab: privacy, imprint
    for (const toggle of inputs().filter((input) => input.attributes('mandatory') !== undefined)) toggle.vm.$emit('update:modelValue', 'text')
    await flushPromises()
    const editors = inputs().filter((input) => input.classes('rich-editor-scroll'))
    expect(editors).toHaveLength(2)
    editors[0]!.vm.$emit('update:modelValue', '<p>Datenschutz</p>')
    editors[1]!.vm.$emit('update:modelValue', '<p>Impressum</p>')

    await wrapper.find('v-form').trigger('submit')
    expect(api.put).toHaveBeenCalledWith('/api/settings', expect.objectContaining({
      contactName: 'Ada', contactEmail: 'info@example.test',
      privacyPolicyMode: 'text', privacyPolicyUrl: 'https://example.test/datenschutz', privacyPolicyHtml: '<p>Datenschutz</p>',
      imprintMode: 'text', imprintUrl: 'https://example.test/alt', imprintHtml: '<p>Impressum</p>'
    }))
  })
})
