<script setup lang="ts">
import { computed } from 'vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import CalendarFeedLink from '@/components/CalendarFeedLink.vue'
import { api, toQuery } from '@/services/api'
import type { CalendarFeedTokenDto } from '@/types/api'

const props = defineProps<{ entityType: 'club' | 'department'; entityId: string; readonly?: boolean }>()

const queryClient = useQueryClient()
const scope = computed(() => (props.entityType === 'club' ? 0 : 1))

const queryKey = computed(() => ['calendar-feeds', props.entityType, props.entityId])

const tokens = useQuery({
  queryKey,
  queryFn: ({ signal }) =>
    api.get<CalendarFeedTokenDto[]>(
      `/api/calendar-feeds/tokens${toQuery(
        props.entityType === 'club'
          ? { clubId: props.entityId }
          : { departmentId: props.entityId }
      )}`,
      signal
    )
})

const variants = computed(() =>
  props.entityType === 'club'
    ? [
        { includeDepartments: false, title: 'Nur Verein' },
        { includeDepartments: true, title: 'Verein mit allen Abteilungen' }
      ]
    : [{ includeDepartments: false, title: 'Abteilungskalender' }]
)

function current(includeDepartments: boolean): CalendarFeedTokenDto | undefined {
  return (tokens.data.value ?? [])
    .filter(
      (item) =>
        !item.isRevoked &&
        Number(item.scope) === scope.value &&
        item.includeDepartments === includeDepartments
    )
    .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))[0]
}

// Read-only users only see the links that actually exist.
const shownVariants = computed(() =>
  props.readonly ? variants.value.filter((v) => current(v.includeDepartments)?.feedUrl) : variants.value
)

function createBody(includeDepartments: boolean): Record<string, unknown> {
  return {
    scope: scope.value,
    includeDepartments,
    clubId: props.entityType === 'club' ? props.entityId : null,
    departmentId: props.entityType === 'department' ? props.entityId : null,
    highlightsOnly: false
  }
}

const refresh = () => queryClient.invalidateQueries({ queryKey: queryKey.value })
</script>

<template>
  <v-card variant="outlined" class="mt-2">
    <v-card-item title="Kalender abonnieren (iCal)" prepend-icon="mdi-calendar-sync" />
    <v-card-text>
      <p class="text-body-medium text-medium-emphasis mb-4">
        Mit diesen Links kannst du den Kalender in Outlook, Apple Kalender, Google Kalender oder
        anderen Anwendungen abonnieren. Kopiere den Link und füge ihn dort als „Kalender-Abonnement“ ein.
        <template v-if="!readonly">Der Link bleibt gültig, bis du ihn erneuerst oder widerrufst.</template>
      </p>

      <v-progress-linear v-if="tokens.isPending.value" indeterminate />
      <v-alert v-else-if="tokens.isError.value" type="warning">
        Kalender-Links konnten nicht geladen werden.
      </v-alert>

      <template v-else>
        <template v-for="(variant, index) in shownVariants" :key="String(variant.includeDepartments)">
          <v-divider v-if="index > 0" class="my-4" />
          <CalendarFeedLink
            :title="variant.title"
            :token="current(variant.includeDepartments)"
            :create-body="createBody(variant.includeDepartments)"
            :readonly="readonly"
            @changed="refresh"
          />
        </template>
      </template>
    </v-card-text>
  </v-card>
</template>
