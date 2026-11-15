import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, type PropType } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import PendingItemsView from '@/views/admin/PendingItemsView.vue'
import { api, ApiError } from '@/services/api'
import type { PendingItemsOverviewDto } from '@/types/api'

const notify = vi.fn()

vi.mock('@/services/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/api')>()),
  api: { get: vi.fn(), post: vi.fn(), delete: vi.fn() }
}))
vi.mock('@/stores/ui', () => ({ useUiStore: () => ({ notify }) }))

const recently = new Date(Date.now() - 60 * 60 * 1000).toISOString()
const overview: PendingItemsOverviewDto = {
  invitations: [{
    id: 'inv-1', email: 'neu@example.org', invitedByName: 'Admin', createdAt: '2026-11-01T10:00:00Z', expiresAt: '2026-11-08T10:00:00Z',
    contextEmail: 'vorstand@tsv.example', pendingClaims: [1, 'DataReader'], clubId: 'club-1', clubName: 'TSV', positionName: 'Vorsitz'
  }, {
    id: 'inv-2', email: 'ohne@example.org', createdAt: '2026-11-01T10:00:00Z', expiresAt: '2026-11-08T10:00:00Z', pendingClaims: []
  }],
  assignments: [
    { assignmentId: 'a-1', userId: 'u-1', userName: 'Erika', userEmail: 'erika@example.org', assignedAt: '2026-11-01T10:00:00Z', departmentId: 'dep-1', clubName: 'TSV', departmentName: 'Handball' },
    { assignmentId: 'a-2', userId: 'u-2', userName: 'Max', userEmail: 'max@example.org', assignedAt: '2026-11-01T10:00:00Z', lastConsentRequestAt: recently, clubId: 'club-1', clubName: 'TSV' }
  ],
  contextEmails: [
    { assignmentId: 'a-3', userId: 'u-3', userName: 'Mia', contextEmail: 'jugend@tsv.example', isAssignmentConfirmed: false, clubId: 'club-1', clubName: 'TSV' }
  ]
}

/** Renders the item slots of each row so that the cell templates and their buttons can be used. */
const DataTableStub = defineComponent({
  props: { items: { type: Array as PropType<Record<string, unknown>[]>, default: () => [] }, headers: { type: Array as PropType<{ key: string }[]>, default: () => [] } },
  setup(props, { slots }) {
    return () => h('div', { class: 'data-table' }, props.items.map((item) => h('div', { class: 'row' }, props.headers.map((header) => slots[`item.${header.key}`]?.({ item })))))
  }
})

type ViewState = { tab: string }

function mountView() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return mount(PendingItemsView, {
    global: {
      plugins: [[VueQueryPlugin, { queryClient }]],
      components: { VDataTable: DataTableStub },
      stubs: { RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' } },
      config: { warnHandler: () => {} }
    }
  })
}

describe('PendingItemsView', () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset().mockResolvedValue(overview)
    vi.mocked(api.post).mockReset().mockResolvedValue(undefined)
    vi.mocked(api.delete).mockReset().mockResolvedValue(undefined)
    notify.mockReset()
  })

  it('lists invitations with organization link, office address and roles', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(api.get).toHaveBeenCalledWith('/api/pending-items', expect.anything())
    expect(wrapper.find('a[href="/clubs/club-1"]').text()).toBe('TSV')
    expect(wrapper.text()).toContain('Amtsadresse: vorstand@tsv.example')
    expect(wrapper.text()).toContain('Manager')
    expect(wrapper.text()).toContain('Leserechte')
    expect(wrapper.text()).toContain('Ohne Zuordnung')
  })

  it('revokes an invitation after confirmation', async () => {
    const wrapper = mountView()
    await flushPromises()

    await wrapper.find('[aria-label="Einladung an neu@example.org widerrufen"]').trigger('click')
    const dialog = wrapper.findComponent(ConfirmDialog)
    expect(dialog.props('modelValue')).toBe(true)
    expect(dialog.props('text')).toContain('neu@example.org')
    dialog.vm.$emit('confirm')
    await flushPromises()

    expect(api.delete).toHaveBeenCalledWith('/api/pending-items/invitations/inv-1')
    expect(notify).toHaveBeenCalledWith('Einladung an neu@example.org wurde widerrufen.', 'success')
    expect(api.get).toHaveBeenCalledTimes(2)
  })

  it('reminds pending assignments and blocks a second reminder within 24 hours', async () => {
    const wrapper = mountView()
    ;(wrapper.vm as unknown as ViewState).tab = 'assignments'
    await flushPromises()

    expect(wrapper.find('a[href="/departments/dep-1"]').exists()).toBe(true)
    expect(wrapper.find('[aria-label="Max an Zustimmung erinnern"]').attributes('disabled')).toBe('true')

    await wrapper.find('[aria-label="Erika an Zustimmung erinnern"]').trigger('click')
    await flushPromises()

    expect(api.post).toHaveBeenCalledWith('/api/users/contact-assignments/a-1/consent-reminder')
    expect(notify).toHaveBeenCalledWith('Erinnerung an Erika wurde versendet.', 'success')
  })

  it('resends the office address confirmation and shows server errors', async () => {
    vi.mocked(api.post).mockRejectedValueOnce(new ApiError('Bitte warte etwas.', 429))
    const wrapper = mountView()
    ;(wrapper.vm as unknown as ViewState).tab = 'contextEmails'
    await flushPromises()

    expect(wrapper.text()).toContain('Zuordnung unbestätigt')
    await wrapper.find('[aria-label="Bestätigung an jugend@tsv.example erneut senden"]').trigger('click')
    await flushPromises()

    expect(api.post).toHaveBeenCalledWith('/api/users/u-3/contact-assignments/a-3/email/resend-verification')
    expect(notify).toHaveBeenCalledWith('Bitte warte etwas.', 'error')
  })

  it('uses the fallback message for unexpected errors', async () => {
    vi.mocked(api.post).mockRejectedValueOnce(new Error('network'))
    const wrapper = mountView()
    ;(wrapper.vm as unknown as ViewState).tab = 'contextEmails'
    await flushPromises()

    await wrapper.find('[aria-label="Bestätigung an jugend@tsv.example erneut senden"]').trigger('click')
    await flushPromises()

    expect(notify).toHaveBeenCalledWith('Bestätigungs-E-Mail konnte nicht versendet werden.', 'error')
  })

  it('shows an error when loading fails', async () => {
    vi.mocked(api.get).mockRejectedValue(new Error('offline'))
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Ausstehende Bestätigungen konnten nicht geladen werden.')
  })
})
