<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api, ApiError } from '@/services/api'
import { passkeysSupported } from '@/services/passkeys'
import { contextEmailError, isRoleEmail, ROLE_EMAIL_HELP_URL } from '@/services/roleEmail'
import { visibilityOptions as visibilities } from '@/services/visibility'
import type { InvitationDetails } from '@/types/api'
import { organizationPath } from '@/services/organizationPath'

/** Matches the server enum LoginMethod (0 = EmailLink, 1 = Passkey). */
const LoginMethod = { EmailLink: 0, Passkey: 1 } as const

const route = useRoute(); const token = typeof route.query.token === 'string' ? route.query.token : ''; const loading = ref(true); const saving = ref(false); const details = ref<InvitationDetails | null>(null); const error = ref(''); const completed = ref(false)
const passkeyAvailable = passkeysSupported()
const form = reactive({ firstName: '', lastName: '', phoneNumber: '', emailVisibility: 1, phoneVisibility: 1, acceptedPrivacyPolicy: false, preferredLoginMethod: passkeyAvailable ? LoginMethod.Passkey : LoginMethod.EmailLink })
/** Optional role / office address per pending assignment, keyed by invitation id (prefilled with the inviter's suggestion). */
const contextEmails = reactive<Record<string, string>>({})
const contextErrors = computed(() => Object.fromEntries(Object.entries(contextEmails).map(([id, value]) => [id, contextEmailError(value, details.value?.email)])))
const hasContextEmail = computed(() => Object.values(contextEmails).some((value) => !!value.trim()))
const loginLooksLikeRole = computed(() => isRoleEmail(details.value?.email))
const valid = computed(() => !!form.firstName.trim() && !!form.lastName.trim() && form.acceptedPrivacyPolicy && Object.values(contextErrors.value).every((message) => !message))
const passkeyRequired = computed(() => details.value?.passkeyRequired === true)
onMounted(async () => { if (!token) { loading.value = false; return } try { details.value = await api.get<InvitationDetails>(`/api/invitations/details?token=${encodeURIComponent(token)}`); if (details.value.passkeyRequired) form.preferredLoginMethod = LoginMethod.Passkey; for (const item of details.value.pendingAssignments) contextEmails[item.invitationId] = item.contextEmail ?? '' } catch { error.value = 'Die Einladung ist ungültig oder abgelaufen.' } finally { loading.value = false } })
async function accept(): Promise<void> { if (!valid.value) return; saving.value = true; try { await api.post('/api/invitations/accept', { token, ...form, phoneNumber: form.phoneNumber || null, assignmentContextEmails: Object.fromEntries(Object.entries(contextEmails).map(([id, value]) => [id, value.trim()])) }); completed.value = true } catch (reason) { error.value = reason instanceof ApiError && reason.message ? reason.message : 'Die Registrierung konnte nicht abgeschlossen werden.' } finally { saving.value = false } }
</script>
<template>
  <v-container class="public-page pa-4">
    <v-card width="100%" max-width="650" class="pa-3 pa-sm-7">
      <v-img src="/images/logo-no-bg.svg" height="70" alt="Vernetzt" />
      <v-card-title class="text-center text-wrap"><h1 class="text-headline-large">Willkommen bei Vernetzt</h1></v-card-title>
      <v-progress-linear v-if="loading" indeterminate />
      <v-alert v-else-if="error || !details" type="error">{{ error || 'Der Einladungslink fehlt.' }}</v-alert>
      <v-alert v-else-if="completed" type="success" icon="mdi-email-check">
        <div class="text-title-large">Fast geschafft!</div>
        <p class="my-3">Wir haben dir eine E-Mail an <strong>{{ details.email }}</strong> geschickt. Klicke auf den Link darin, um dich anzumelden.</p>
        <p class="mb-0">Der Link ist nur kurz gültig. Schau bitte auch im Spam-Ordner nach.</p>
        <p v-if="form.preferredLoginMethod === LoginMethod.Passkey" class="mt-3 mb-0">Nach der Anmeldung richten wir gemeinsam deinen Passkey ein.</p>
        <p v-if="hasContextEmail" class="mt-3 mb-0">An jede angegebene Amtsadresse schicken wir zusätzlich eine Bestätigungs-E-Mail. Bis zur Bestätigung gehen Mails zu der Zuordnung an deine persönliche Adresse.</p>
      </v-alert>
      <v-form v-else @submit.prevent="accept">
        <v-card-text>
          <v-alert type="info" variant="tonal" class="mb-4">
            Einladung für <strong>{{ details.email }}</strong>
            <div class="text-body-medium mt-1">Diese Adresse ist dein persönlicher Zugang zu Vernetzt. Das Konto gehört dir, nicht deinem Amt.</div>
          </v-alert>
          <v-alert v-if="loginLooksLikeRole" type="warning" variant="tonal" icon="mdi-account-switch" class="mb-4">
            <strong>{{ details.email }}</strong> sieht nach einer Vereins- oder Amtsadresse aus. Solche Adressen werden oft an die Nachfolge weitergegeben, die dann Zugriff auf dein Konto hätte.
            Bitte die einladende Person, dich mit deiner <strong>persönlichen</strong> Adresse neu einzuladen. Die Amtsadresse kannst du dann für deine Zuordnung hinterlegen.
            <a :href="ROLE_EMAIL_HELP_URL" target="_blank" rel="noopener noreferrer">Mehr erfahren</a>
          </v-alert>
          <template v-if="details.pendingAssignments.length">
            <h2 class="text-title-large mb-1">Deine Zuordnungen</h2>
            <p class="text-body-medium text-medium-emphasis mb-3">
              Hast du für eine Zuordnung eine Vereins- oder Amtsadresse (z. B. vorstand@verein.de)? Dann trage sie hier ein. Mails zu dieser Zuordnung gehen nach der Bestätigung dorthin, alles andere an deine persönliche Adresse.
              <a :href="ROLE_EMAIL_HELP_URL" target="_blank" rel="noopener noreferrer">Mehr erfahren</a>
            </p>
            <v-card v-for="item in details.pendingAssignments" :key="item.invitationId" variant="outlined" class="mb-3">
              <v-list-item prepend-icon="mdi-account-group" :title="item.departmentName ?? item.clubName ?? 'Zuordnung'" :subtitle="[item.departmentName ? organizationPath({ clubName: item.clubName, parentDepartmentName: item.parentDepartmentName }) : null, item.positionName].filter(Boolean).join(' · ') || undefined" />
              <v-card-text class="pt-0 pb-2">
                <v-text-field v-model="contextEmails[item.invitationId]" label="Amtsadresse für diese Zuordnung (optional)" placeholder="z. B. vorstand@verein.de" type="email" autocomplete="off" prepend-inner-icon="mdi-briefcase-outline" :error-messages="contextErrors[item.invitationId]" :hint="item.contextEmail ? 'Von der einladenden Person vorgeschlagen – du kannst sie ändern oder leeren.' : undefined" :persistent-hint="!!item.contextEmail" />
              </v-card-text>
            </v-card>
          </template>
          <v-row><v-col cols="12" sm="6"><v-text-field v-model="form.firstName" label="Vorname" required /></v-col><v-col cols="12" sm="6"><v-text-field v-model="form.lastName" label="Nachname" required /></v-col></v-row>
          <v-text-field v-model="form.phoneNumber" label="Telefonnummer (optional)" />
          <v-select v-model="form.emailVisibility" label="E-Mail sichtbar für" :items="visibilities" />
          <v-select v-model="form.phoneVisibility" label="Telefonnummer sichtbar für" :items="visibilities" />

          <h2 class="text-title-large mt-4 mb-2">Wie möchtest du dich künftig anmelden?</h2>
          <template v-if="passkeyRequired">
            <v-alert type="info" variant="tonal" icon="mdi-key" class="mb-4">
              <strong>Mit Passkey</strong> – für deine Rolle ist die Anmeldung per Passkey vorgeschrieben, eine Anmeldung per E-Mail-Link ist nicht möglich.
              Nach der Registrierung erhältst du einmalig einen Anmeldelink per E-Mail und richtest danach deinen Passkey ein (Fingerabdruck, Gesicht oder Geräte-PIN).
            </v-alert>
            <v-alert v-if="!passkeyAvailable" type="warning" variant="tonal" class="mb-4">Dieser Browser unterstützt keine Passkeys. Bitte öffne den Anmeldelink später auf einem Gerät mit Passkey-Unterstützung.</v-alert>
          </template>
          <template v-else>
            <v-radio-group v-model="form.preferredLoginMethod" hide-details class="mb-2">
              <v-radio v-if="passkeyAvailable" :value="LoginMethod.Passkey">
                <template #label><div><strong>Mit Passkey (empfohlen)</strong><div class="text-body-medium text-medium-emphasis">Schnell und sicher mit Fingerabdruck, Gesicht oder Geräte-PIN – ganz ohne E-Mail.</div></div></template>
              </v-radio>
              <v-radio :value="LoginMethod.EmailLink">
                <template #label><div><strong>Mit Anmeldelink per E-Mail</strong><div class="text-body-medium text-medium-emphasis">Du bekommst bei jeder Anmeldung einen Link an deine E-Mail-Adresse.</div></div></template>
              </v-radio>
            </v-radio-group>
            <p class="text-body-medium text-medium-emphasis mb-4">Du kannst das später jederzeit unter „Mein Konto“ ändern.</p>
          </template>

          <v-checkbox v-model="form.acceptedPrivacyPolicy"><template #label>Ich akzeptiere die&nbsp;<a :href="details.privacyPolicyUrl" target="_blank" rel="noopener noreferrer" @click.stop>Datenschutzerklärung</a>.</template></v-checkbox>
        </v-card-text>
        <v-card-actions><v-spacer /><v-btn type="submit" color="primary" size="large" variant="flat" :disabled="!valid" :loading="saving">Registrierung abschließen</v-btn></v-card-actions>
      </v-form>
    </v-card>
  </v-container>
</template>
