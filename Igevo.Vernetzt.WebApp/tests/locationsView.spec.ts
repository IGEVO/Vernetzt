import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import LocationsView from '@/views/admin/LocationsView.vue'
import { api, ApiError } from '@/services/api'
import type { LocationDto } from '@/types/api'

const notify = vi.fn()

vi.mock('@/services/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/api')>()),
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }
}))
vi.mock('@/stores/ui', () => ({ useUiStore: () => ({ notify }) }))

const locations: LocationDto[] = [
  { id: 'loc-1', name: 'Sporthalle Nord', street: 'Nordring 5', postalCode: '44787', city: 'Bochum', description: 'Dreifachhalle', usageCount: 2 },
  { id: 'loc-2', name: 'Hallenbad', city: 'Bochum', usageCount: 0 }
]

type Form = { name: string; description: string; street: string; postalCode: string; city: string }
type ViewState = { dialog: boolean; editing: LocationDto | null; form: Form }

function mountView() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrapper = mount(LocationsView, {
    global: { plugins: [[VueQueryPlugin, { queryClient }]], config: { warnHandler: () => {} } }
  })
  return { wrapper, state: wrapper.vm as unknown as ViewState }
}

describe('LocationsView', () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset().mockResolvedValue(locations)
    vi.mocked(api.post).mockReset().mockResolvedValue(undefined)
    vi.mocked(api.put).mockReset().mockResolvedValue(undefined)
    vi.mocked(api.delete).mockReset().mockResolvedValue(undefined)
    notify.mockReset()
  })

  it('lists locations with a formatted address', async () => {
    const { wrapper } = mountView()
    await flushPromises()

    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('Nordring 5, 44787 Bochum')
    expect(rows[1]!.text()).toContain('Bochum')
    expect(wrapper.findAll('th[scope="col"]')).toHaveLength(5)
  })

  it('shows a hint when there are no locations', async () => {
    vi.mocked(api.get).mockResolvedValue([])
    const { wrapper } = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Noch keine Standorte angelegt.')
  })

  it('creates a location with trimmed values', async () => {
    const { wrapper, state } = mountView()
    await flushPromises()

    await wrapper.find('v-btn[prepend-icon="mdi-plus"]').trigger('click')
    expect(state.dialog).toBe(true)
    expect(state.editing).toBeNull()
    Object.assign(state.form, { name: '  Turnhalle  ', description: ' ', street: '', postalCode: '44799', city: ' Bochum ' })
    await wrapper.find('v-form').trigger('submit')
    await flushPromises()

    expect(api.post).toHaveBeenCalledWith('/api/locations', { name: 'Turnhalle', description: null, street: null, postalCode: '44799', city: 'Bochum' })
    expect(state.dialog).toBe(false)
    expect(notify).toHaveBeenCalledWith('Standort wurde gespeichert.', 'success')
  })

  it('does not save without a name', async () => {
    const { wrapper, state } = mountView()
    await flushPromises()

    await wrapper.find('v-btn[prepend-icon="mdi-plus"]').trigger('click')
    state.form.name = '   '
    await wrapper.find('v-form').trigger('submit')

    expect(api.post).not.toHaveBeenCalled()
  })

  it('edits a location and shows validation errors from the server', async () => {
    vi.mocked(api.put).mockRejectedValueOnce(new ApiError('Ein Standort mit diesem Namen existiert bereits.', 400))
    const { wrapper, state } = mountView()
    await flushPromises()

    await wrapper.find('[aria-label="Sporthalle Nord bearbeiten"]').trigger('click')
    expect(state.form).toEqual({ name: 'Sporthalle Nord', description: 'Dreifachhalle', street: 'Nordring 5', postalCode: '44787', city: 'Bochum' })
    state.form.name = 'Hallenbad'
    await wrapper.find('v-form').trigger('submit')
    await flushPromises()

    expect(api.put).toHaveBeenCalledWith('/api/locations/loc-1', expect.objectContaining({ name: 'Hallenbad' }))
    expect(notify).toHaveBeenCalledWith('Ein Standort mit diesem Namen existiert bereits.', 'error')
    expect(state.dialog).toBe(true)
  })

  it('uses a generic message for other save errors', async () => {
    vi.mocked(api.post).mockRejectedValueOnce(new Error('offline'))
    const { wrapper, state } = mountView()
    await flushPromises()

    await wrapper.find('v-btn[prepend-icon="mdi-plus"]').trigger('click')
    state.form.name = 'Neu'
    await wrapper.find('v-form').trigger('submit')
    await flushPromises()

    expect(notify).toHaveBeenCalledWith('Standort konnte nicht gespeichert werden.', 'error')
  })

  it('warns about usage before deleting and deletes after confirmation', async () => {
    const { wrapper } = mountView()
    await flushPromises()

    await wrapper.find('[aria-label="Sporthalle Nord löschen"]').trigger('click')
    const dialog = wrapper.findComponent(ConfirmDialog)
    expect(dialog.props('text')).toContain('von 2 Organisationen genutzt')
    dialog.vm.$emit('confirm')
    await flushPromises()

    expect(api.delete).toHaveBeenCalledWith('/api/locations/loc-1')
    expect(notify).toHaveBeenCalledWith('Standort wurde gelöscht.', 'success')
  })

  it('reports failed deletions', async () => {
    vi.mocked(api.delete).mockRejectedValueOnce(new Error('offline'))
    const { wrapper } = mountView()
    await flushPromises()

    await wrapper.find('[aria-label="Hallenbad löschen"]').trigger('click')
    const dialog = wrapper.findComponent(ConfirmDialog)
    expect(dialog.props('text')).toBe('Diese Aktion kann nicht rückgängig gemacht werden.')
    dialog.vm.$emit('confirm')
    await flushPromises()

    expect(notify).toHaveBeenCalledWith('Standort konnte nicht gelöscht werden.', 'error')
  })
})
