<script setup lang="ts">
import type { PublicContactDetails } from '~/types/public'

/**
 * Email addresses and phone numbers are not part of the page or its payload. They are loaded
 * from a strictly rate limited endpoint only when a visitor asks for them, so crawlers that
 * read the HTML or the public API in bulk never see them.
 */
const props = defineProps<{
  /** Public API path of the contact-details endpoint. */
  source: string
  hasEmail?: boolean
  hasPhoneNumber?: boolean
  /** Whose contact data is revealed; extends the button name for screen readers (e.g. "Petra Publik"). */
  owner: string
}>()

const details = ref<PublicContactDetails | null>(null)
const pending = ref(false)
const error = ref<string | null>(null)
const container = ref<HTMLElement | null>(null)

const buttonText = computed(() => {
  if (props.hasEmail && props.hasPhoneNumber) return 'E-Mail und Telefon anzeigen'
  return props.hasEmail ? 'E-Mail anzeigen' : 'Telefon anzeigen'
})

async function reveal() {
  if (pending.value) return
  pending.value = true
  error.value = null
  try {
    details.value = await $fetch<PublicContactDetails>(props.source)
    await nextTick()
    // Keep keyboard and screen reader users in place: the button they pressed is gone now.
    container.value?.querySelector<HTMLElement>('a')?.focus()
  } catch (cause) {
    const status = (cause as { statusCode?: number })?.statusCode
    error.value = status === 429
      ? 'Zu viele Abrufe. Bitte in einer Minute erneut versuchen.'
      : 'Die Kontaktdaten konnten nicht geladen werden.'
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <div v-if="hasEmail || hasPhoneNumber" ref="container" class="contact-reveal">
    <template v-if="details">
      <a v-if="details.email" :href="`mailto:${details.email}`"><AppIcon name="mail" />{{ details.email }}</a>
      <a v-if="details.phoneNumber" :href="`tel:${details.phoneNumber.replace(/[^\d+]/g, '')}`"><AppIcon name="phone" />{{ details.phoneNumber }}</a>
    </template>
    <template v-else>
      <button type="button" class="reveal-button" :aria-busy="pending" :disabled="pending" @click="reveal">
        <AppIcon :name="hasEmail ? 'mail' : 'phone'" />
        <span>{{ pending ? 'Wird geladen …' : buttonText }}<span class="visually-hidden"> ({{ owner }})</span></span>
      </button>
      <p v-if="error" class="reveal-error" role="alert">{{ error }}</p>
    </template>
  </div>
</template>
