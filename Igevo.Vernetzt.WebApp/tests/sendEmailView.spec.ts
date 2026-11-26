import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import SendEmailView from '@/views/email/SendEmailView.vue'
import { api } from '@/services/api'
import type { ClubDto, RecipientCandidateDto } from '@/types/api'

const notify = vi.fn()
const push = vi.fn()
const roles = { manager: false }

vi.mock('@/services/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/api')>()),
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }
}))
vi.mock('@/stores/ui', () => ({ useUiStore: () => ({ notify }) }))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ hasRole: () => roles.manager }) }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

const TSV = 'club-tsv'; const HARMONIE = 'club-harmonie'
const person = (overrides: Partial<RecipientCandidateDto>): RecipientCandidateDto => ({
  assignmentId: '', isAssignmentRecipient: true, userId: '', firstName: '', lastName: '', organizationId: TSV, organizationName: 'TSV',
  organizationType: 0, clubTypeId: 'ct-sport', ...overrides
})
/** TSV: Kai is treasurer, Max chair. Harmonie: only Lea as chair. */
const candidates = [
  person({ assignmentId: 'as-kai', userId: 'u-kai', firstName: 'Kai', positionId: 'pos-treasurer', displayEmail: 'kai@example.test' }),
  person({ assignmentId: 'as-max', userId: 'u-max', firstName: 'Max', positionId: 'pos-chair', displayEmail: 'max@example.test' }),
  person({ assignmentId: 'as-lea', userId: 'u-lea', firstName: 'Lea', organizationId: HARMONIE, organizationName: 'Harmonie', clubTypeId: 'ct-music', positionId: 'pos-chair', displayEmail: 'lea@example.test' })
]
const club = (overrides: Partial<ClubDto>): ClubDto => ({ id: TSV, name: 'TSV', clubTypeId: 'ct-sport', email: 'info@tsv.test', hasAvatar: false, departmentCount: 0, ...overrides }) as ClubDto
let clubs: ClubDto[] = []

type Item = RecipientCandidateDto & { clubAddress?: true }
type ViewState = {
  step: number; filterPositions: string[]; filterClubTypes: string[]; recipientKinds: Array<'persons' | 'clubs'>
  fallbackPosition: string | null; fallbackClubAddress: boolean; selected: Set<string>; subject: string; html: string
  filterResult: { matches: Item[]; fallbackKeys: Set<string> }; fallbackKeys: Set<string>
  coverage: { total: number; reached: number; missing: ClubDto[] }; clubItems: Item[]; relayAvailable: boolean
  canAddressAll: boolean; text: () => string; toStep2: () => void; selectAllPersons: () => void; send: () => Promise<void>; createRelay: () => Promise<void>; method: 'simple' | 'relay'; resetFilters: () => void
}

async function mountView() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  // Vuetify is not installed in unit tests: render every stepper step so the template bindings are evaluated
  const stepper = { template: '<div><slot name="item.1" /><slot name="item.2" /><slot name="item.3" /><slot name="item.4" /></div>' }
  const wrapper = mount(SendEmailView, { global: { plugins: [[VueQueryPlugin, { queryClient }]], stubs: { 'v-stepper': stepper, 'v-item': { template: '<div><slot :is-selected="false" :toggle="() => {}" /></div>' } }, config: { warnHandler: () => {} } } })
  await flushPromises()
  return Object.assign(wrapper.vm as unknown as ViewState, { text: () => wrapper.text() })
}
const keysOf = (items: Item[]) => items.map((item) => item.clubAddress ? `club:${item.organizationId}` : item.assignmentId).sort()

describe('SendEmailView recipient selection', () => {
  beforeEach(() => {
    roles.manager = false
    clubs = [club({}), club({ id: HARMONIE, name: 'Harmonie', clubTypeId: 'ct-music', email: null })]
    vi.mocked(api.get).mockReset().mockImplementation(async (url: string) => {
      if (url === '/api/users/recipient-candidates') return candidates
      if (url === '/api/clubs') return clubs
      if (url === '/api/email/relay/status') return true
      return []
    })
    vi.mocked(api.post).mockReset().mockResolvedValue({ success: true, recipientCount: 1 })
    notify.mockReset(); push.mockReset()
  })

  it('reports clubs that drop out of a position filter', async () => {
    const view = await mountView()
    view.filterPositions = ['pos-treasurer']
    await flushPromises()
    expect(keysOf(view.filterResult.matches)).toEqual(['as-kai'])

    view.toStep2()
    await flushPromises()
    expect(view.coverage.reached).toBe(1)
    expect(view.coverage.missing.map((c) => c.name)).toEqual(['Harmonie'])
    expect(view.text()).toContain('1 von 2 Vereinen erreicht.')
    expect(view.text()).toContain('Tipp: Wähle im ersten Schritt eine Ersatzposition')
  })

  it('uses the fallback position for clubs without the filtered position', async () => {
    const view = await mountView()
    view.filterPositions = ['pos-treasurer']
    view.fallbackPosition = 'pos-chair'
    await flushPromises()
    // TSV keeps Kai (Max is not a fallback there), Harmonie is reached via Lea
    expect(keysOf(view.filterResult.matches)).toEqual(['as-kai', 'as-lea'])
    expect([...view.filterResult.fallbackKeys]).toEqual(['as-lea'])
    expect(view.text()).toContain('davon 1 als Ersatz')

    view.toStep2()
    expect(view.fallbackKeys.has('as-lea')).toBe(true)
    expect(view.coverage).toMatchObject({ total: 2, reached: 2, missing: [] })
  })

  it('clears the fallback when the position filter is removed', async () => {
    const view = await mountView()
    view.filterPositions = ['pos-treasurer']; view.fallbackPosition = 'pos-chair'
    await flushPromises()
    view.filterPositions = []
    await flushPromises()
    expect(view.fallbackPosition).toBeNull()
  })

  it('sends persons by assignment and keeps the relay for them', async () => {
    const view = await mountView()
    view.filterPositions = ['pos-treasurer']
    await flushPromises()
    view.toStep2()
    await flushPromises()
    expect(view.relayAvailable).toBe(true)

    view.method = 'relay'
    await view.createRelay()
    expect(api.post).toHaveBeenCalledWith('/api/email/relay/token', { recipientAssignmentIds: ['as-kai'], recipientUserIds: [], subjectHint: null })

    view.subject = 'Kasse'; view.html = '<p>Hallo</p>'
    await view.send()
    expect(api.post).toHaveBeenLastCalledWith('/api/email/bulk', expect.objectContaining({ recipientAssignmentIds: ['as-kai'], recipientUserIds: [], recipientClubIds: [] }))
  })

  it('hides club addresses from regular users', async () => {
    const view = await mountView()
    expect(view.canAddressAll).toBe(false)
    expect(view.clubItems).toEqual([])
    expect(view.text()).not.toContain('Vereinsadressen (')
    expect(view.text()).not.toContain('Alle Personen')
  })

  it('lets managers send to club addresses only', async () => {
    roles.manager = true
    const view = await mountView()
    expect(keysOf(view.clubItems)).toEqual([`club:${TSV}`])
    view.recipientKinds = ['clubs']
    await flushPromises()
    expect(keysOf(view.filterResult.matches)).toEqual([`club:${TSV}`])

    view.toStep2()
    await flushPromises()
    expect(view.relayAvailable).toBe(false)
    expect(view.text()).toContain('Für Vereinsadressen derzeit nicht verfügbar')
    expect(view.text()).toContain('Adresse aus den Vereinsstammdaten')
    expect(view.coverage.missing.map((c) => c.name)).toEqual(['Harmonie'])

    view.subject = 'Rundschreiben'; view.html = '<p>Hallo</p>'
    await view.send()
    expect(api.post).toHaveBeenCalledWith('/api/email/bulk', expect.objectContaining({ recipientClubIds: [TSV], recipientAssignmentIds: [], recipientUserIds: [] }))
    expect(push).toHaveBeenCalledWith('/')
  })

  it('falls back to the club address when no fallback person exists', async () => {
    roles.manager = true
    clubs = [club({}), club({ id: HARMONIE, name: 'Harmonie', clubTypeId: 'ct-music', email: 'info@harmonie.test' })]
    const view = await mountView()
    view.filterPositions = ['pos-treasurer']; view.fallbackClubAddress = true
    await flushPromises()
    expect(keysOf(view.filterResult.matches)).toEqual(['as-kai', `club:${HARMONIE}`])
    expect([...view.filterResult.fallbackKeys]).toEqual([`club:${HARMONIE}`])
  })

  it('selects all persons at once', async () => {
    roles.manager = true
    const view = await mountView()
    view.recipientKinds = ['clubs']
    view.selectAllPersons()
    await flushPromises()
    expect([...view.selected].sort()).toEqual(['as-kai', 'as-lea', 'as-max'])
    expect(view.recipientKinds).toEqual(['persons'])
    expect(view.step).toBe(2)
  })
})
