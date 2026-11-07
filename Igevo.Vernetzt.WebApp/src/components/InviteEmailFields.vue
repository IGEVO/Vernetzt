<script setup lang="ts">
import { computed } from 'vue'
import { contextEmailError, EMAIL_PATTERN, isRoleEmail, ROLE_EMAIL_HELP_URL } from '@/services/roleEmail'

/**
 * E-mail fields for an invitation: the personal login address and – with an assignment – an optional
 * role / office address (e.g. vorstand@verein.de) that becomes the assignment's context email.
 * Warns when the login address looks like a role address and offers to move it into the role field.
 * `part` renders only the login or only the role field, so a dialog can place them in different sections.
 */
const props = withDefaults(defineProps<{ withAssignment: boolean; part?: 'all' | 'login' | 'context' }>(), { part: 'all' })
const email = defineModel<string>('email', { required: true })
const contextEmail = defineModel<string>('contextEmail', { default: '' })

const emailInvalid = computed(() => !!email.value.trim() && !EMAIL_PATTERN.test(email.value.trim()))
const looksLikeRole = computed(() => !emailInvalid.value && isRoleEmail(email.value))
const contextError = computed(() => contextEmailError(contextEmail.value, email.value))

function moveToRoleAddress(): void {
  contextEmail.value = email.value.trim()
  email.value = ''
}
</script>

<template>
  <template v-if="props.part !== 'context'">
    <v-text-field
      v-model="email"
      label="Persönliche E-Mail-Adresse *"
      type="email"
      autocomplete="off"
      hint="Damit meldet sich die Person an. Das Konto gehört ihr persönlich, nicht dem Amt."
      persistent-hint
      :error-messages="emailInvalid ? 'Bitte eine gültige E-Mail-Adresse eingeben' : undefined"
      class="mb-2"
    />
    <v-alert v-if="looksLikeRole" type="warning" variant="tonal" density="compact" icon="mdi-account-switch" class="mb-4">
      <strong>{{ email.trim() }}</strong> sieht nach einer Amtsadresse aus. Solche Adressen werden oft an die Nachfolge weitergegeben, die dann Zugriff auf dieses Konto hätte.
      <template v-if="props.withAssignment"> Trage sie besser als Amtsadresse ein und gib hier die persönliche Adresse der Person an.</template>
      <template v-else> Bitte die persönliche Adresse der Person verwenden. Wird die Person einem Verein, einer Abteilung oder einer Gruppe zugeordnet, kann die Amtsadresse dort zusätzlich angegeben werden.</template>
      <div class="d-flex flex-wrap ga-2 mt-2">
        <v-btn v-if="props.withAssignment" size="small" variant="flat" color="warning" prepend-icon="mdi-arrow-down" @click="moveToRoleAddress">Als Amtsadresse übernehmen</v-btn>
        <v-btn size="small" variant="text" :href="ROLE_EMAIL_HELP_URL" target="_blank" rel="noopener noreferrer" append-icon="mdi-open-in-new">Mehr erfahren</v-btn>
      </div>
    </v-alert>
  </template>
  <v-text-field
    v-if="props.withAssignment && props.part !== 'login'"
    v-model="contextEmail"
    label="Amtsadresse für diese Zuordnung (optional)"
    placeholder="z. B. vorstand@verein.de"
    type="email"
    autocomplete="off"
    hint="Mails zu dieser Zuordnung gehen an diese Adresse, sobald sie bestätigt wurde. Ohne Amtsadresse wird die persönliche Adresse genutzt."
    persistent-hint
    :error-messages="contextError"
    prepend-inner-icon="mdi-briefcase-outline"
    class="mb-2"
  />
</template>
