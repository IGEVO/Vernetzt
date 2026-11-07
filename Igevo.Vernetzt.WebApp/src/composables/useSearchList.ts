import { onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

/** Debounced search term and page, both mirrored into the URL (?q=…&page=…) – shared by all searchable list pages. */
export function useSearchList(delay = 300) {
  const route = useRoute()
  const router = useRouter()
  const search = ref(typeof route.query.q === 'string' ? route.query.q : '')
  const debouncedSearch = ref(search.value)
  const page = ref(Number(route.query.page) || 1)
  let timer: number | undefined

  function syncUrl(): void {
    void router.replace({ query: { ...route.query, q: search.value || undefined, page: page.value > 1 ? String(page.value) : undefined } })
  }
  watch(search, () => {
    clearTimeout(timer)
    timer = window.setTimeout(() => { debouncedSearch.value = (search.value ?? '').trim(); page.value = 1; syncUrl() }, delay)
  })
  watch(page, () => { syncUrl(); window.scrollTo({ top: 0 }) })
  onBeforeUnmount(() => clearTimeout(timer))

  return { search, debouncedSearch, page }
}
