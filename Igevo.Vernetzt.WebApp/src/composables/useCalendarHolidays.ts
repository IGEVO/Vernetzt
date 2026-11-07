import { computed, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { api, toQuery } from '@/services/api'
import { yearRange } from '@/services/holidays'
import type { HolidayEntryDto } from '@/types/api'

/**
 * Holidays for the month shown in `EventCalendarGrid`: loaded per calendar year, so paging through
 * the months of one year needs a single request. Wire `onViewMonthChange` to the grid's event.
 */
export function useCalendarHolidays() {
  const year = ref(new Date().getFullYear())

  const query = useQuery({
    queryKey: computed(() => ['holidays', year.value]),
    queryFn: ({ signal }) => api.get<HolidayEntryDto[]>(`/api/holidays${toQuery(yearRange(year.value))}`, signal),
    staleTime: 10 * 60_000
  })

  // A failed request only hides the background layer; the calendar itself stays usable.
  const holidays = computed(() => query.data.value ?? [])

  function onViewMonthChange(payload: { year: number; month: number }): void {
    year.value = payload.year
  }

  return { holidays, onViewMonthChange }
}
