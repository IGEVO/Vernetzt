import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

export type OrganizationSection = 'overview' | 'contacts' | 'events'

/** Active tab of a club / department page, kept in the URL (?tab=contacts|events). */
export function useOrganizationTab() {
  const route = useRoute()
  const router = useRouter()
  return computed<OrganizationSection>({
    get: () => route.query.tab === 'contacts' || route.query.tab === 'events' ? route.query.tab : 'overview',
    set: (tab) => { void router.push({ path: route.path, query: tab === 'overview' ? {} : { tab } }) }
  })
}
