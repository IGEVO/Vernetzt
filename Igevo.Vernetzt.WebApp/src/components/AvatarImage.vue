<script setup lang="ts">
import { computed } from 'vue'
import { avatarUrl, type AvatarOwner } from '@/services/avatars'

/** Read-only avatar used in lists, cards and menus: image if available, otherwise initials or an icon. */
const props = withDefaults(defineProps<{
  type: AvatarOwner
  id?: string | null
  hasAvatar?: boolean
  initials?: string
  icon?: string
  size?: number | string
  color?: string
}>(), { id: null, hasAvatar: false, initials: '', icon: undefined, size: 40, color: 'primary' })

const fallbackIcon = computed(() => props.icon ?? (props.type === 'users' ? 'mdi-account' : props.type === 'clubs' ? 'mdi-account-group' : 'mdi-source-branch'))
const textClass = computed(() => Number(props.size) >= 96 ? 'text-headline-medium' : Number(props.size) >= 56 ? 'text-title-large' : 'text-body-medium font-weight-medium')
</script>

<template>
  <v-avatar :size="size" :color="color">
    <v-img v-if="hasAvatar" :src="avatarUrl(type, id)" alt="" cover />
    <span v-else-if="initials" :class="textClass" aria-hidden="true">{{ initials }}</span>
    <v-icon v-else :icon="fallbackIcon" aria-hidden="true" />
  </v-avatar>
</template>
