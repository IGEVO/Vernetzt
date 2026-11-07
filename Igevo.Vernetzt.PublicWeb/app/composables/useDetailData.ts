import type { Ref } from 'vue'

type EntityKind = 'vereine' | 'abteilungen' | 'veranstaltungen'

/**
 * Loads a detail entity, maps API failures to proper error pages (404 vs. 503 instead of an
 * empty 200 page) and redirects to the canonical slug URL.
 */
export async function useDetailData<T extends { name?: string | null; title?: string | null }>(
  kind: EntityKind,
  apiPath: string,
  notFoundMessage: string
): Promise<{ id: string; data: Ref<T> }> {
  // Composables lose the Nuxt context after an await; capture it up front.
  const nuxtApp = useNuxtApp()
  const route = useRoute()
  const id = String(route.params.id)
  if (!isGuid(id)) throw createError({ statusCode: 404, statusMessage: notFoundMessage, fatal: true })

  const { data, error } = await useFetch<T>(`/api/public/v1/${apiPath}/${id}`, { key: `${kind}:${id}` })
  if (error.value || !data.value) {
    const statusCode = error.value?.statusCode === 404 ? 404 : 503
    throw createError({ statusCode, statusMessage: statusCode === 404 ? notFoundMessage : 'Daten nicht erreichbar', fatal: true })
  }

  const expected = entityPath(kind, id, data.value.name ?? data.value.title ?? '')
  if (route.path !== expected) await nuxtApp.runWithContext(() => navigateTo(expected, { redirectCode: 301 }))

  return { id, data: data as Ref<T> }
}
