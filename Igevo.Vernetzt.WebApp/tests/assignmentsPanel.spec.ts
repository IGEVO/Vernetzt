import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, type PropType } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import AssignmentsPanel from '@/components/account/AssignmentsPanel.vue'
import { api } from '@/services/api'
import type { MyContactAssignmentDto, VisibilityLevel } from '@/types/api'

vi.mock('@/services/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/api')>()),
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn() }
}))
vi.mock('@/stores/ui', () => ({ useUiStore: () => ({ notify: vi.fn() }) }))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ user: { email: 'erika@privat.example' } }) }))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }) }))

/** Minimal v-select: exposes label, value and disabled options, and emits the chosen value. */
const SelectStub = defineComponent({
  props: { modelValue: { type: [Number, String, null] as PropType<VisibilityLevel | null>, default: null }, label: { type: String, default: '' }, items: { type: Array as PropType<Array<{ value: VisibilityLevel; disabled?: boolean }>>, default: () => [] } },
  emits: ['update:modelValue'],
  setup(props) {
    return () => h('div', { class: 'select', 'data-label': props.label, 'data-value': String(props.modelValue), 'data-disabled': props.items.filter((item) => item.disabled).map((item) => item.value).join(',') })
  }
})

const assignment = (overrides: Partial<MyContactAssignmentDto> = {}): MyContactAssignmentDto => ({
  assignmentId: 'a-1', organizationId: 'club-1', organizationName: 'TSV', organizationType: 0, contextEmail: 'vorstand@tsv.example',
  contextEmailVerified: true, visibility: 2, contextEmailVisibility: 1, isConfirmed: true, ...overrides
})

async function mountPanel(items: MyContactAssignmentDto[]) {
  vi.mocked(api.get).mockResolvedValue(items)
  const wrapper = mount(AssignmentsPanel, {
    global: {
      plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }]],
      components: { VSelect: SelectStub },
      stubs: { ConfirmDialog: true, RouterLink: true },
      config: { warnHandler: () => {} }
    }
  })
  await flushPromises()
  return wrapper
}

const select = (wrapper: Awaited<ReturnType<typeof mountPanel>>, label: string) => wrapper.findAllComponents(SelectStub).find((item) => item.props('label') === label)!

describe('AssignmentsPanel', () => {
  beforeEach(() => { vi.mocked(api.put).mockReset().mockResolvedValue({ success: true }); vi.mocked(api.post).mockReset().mockResolvedValue({}) })

  it('sends both visibilities and caps the role address when the assignment becomes stricter', async () => {
    const wrapper = await mountPanel([assignment()])
    expect(select(wrapper, 'Amtsadresse sichtbar für').attributes('data-value')).toBe('1')
    expect(select(wrapper, 'Amtsadresse sichtbar für').attributes('data-disabled')).toBe('')
    expect(wrapper.text()).toContain('Amtsadresse: Alle registrierten Benutzer')

    select(wrapper, 'Zuordnung sichtbar für').vm.$emit('update:modelValue', 0)
    await flushPromises()
    expect(select(wrapper, 'Amtsadresse sichtbar für').attributes('data-value')).toBe('0')
    expect(select(wrapper, 'Amtsadresse sichtbar für').attributes('data-disabled')).toBe('1,2')

    await wrapper.findAll('v-form')[0]!.trigger('submit')
    await flushPromises()
    expect(api.put).toHaveBeenCalledWith('/api/users/me/contact-assignments/a-1/email', { contextEmail: 'vorstand@tsv.example', visibility: 0, contextEmailVisibility: 0 })
  })

  it('lets the role address of a pending assignment follow the assignment unless chosen separately', async () => {
    const wrapper = await mountPanel([assignment({ isConfirmed: false, visibility: 0, contextEmailVisibility: 0 })])
    const label = 'Amtsadresse vorstand@tsv.example sichtbar für'

    select(wrapper, 'Zuordnung sichtbar für *').vm.$emit('update:modelValue', 2)
    await flushPromises()
    expect(select(wrapper, label).attributes('data-value')).toBe('null')

    await wrapper.findAll('v-form')[0]!.trigger('submit')
    await flushPromises()
    expect(api.post).toHaveBeenCalledWith('/api/users/me/contact-assignments/a-1/confirm', { visibility: 2, contextEmailVisibility: 2 })
  })
})
