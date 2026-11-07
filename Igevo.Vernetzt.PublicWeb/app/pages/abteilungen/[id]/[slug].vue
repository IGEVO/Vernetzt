<script setup lang="ts">
import type { PublicDepartment, PublicEventPage } from '~/types/public'

/** Page for departments and groups (a group is a department below a department). */
const config = useRuntimeConfig()
const { id, data: department } = await useDetailData<PublicDepartment>('abteilungen', 'departments', 'Abteilung nicht gefunden')

const { data: events } = await useFetch<PublicEventPage>('/api/public/v1/events', {
  key: `department-events:${id}`,
  query: { departmentId: id, fromUtc: berlinDayBoundaryUtc(todayIsoDate()), pageSize: 5 }
})

const parent = computed(() => department.value.parentDepartment ?? null)
const isGroup = computed(() => !!parent.value)
const canonical = useCanonical(computed(() => entityPath('abteilungen', id, department.value.name ?? '')))
const clubPath = computed(() => entityPath('vereine', department.value.clubId ?? '', department.value.clubName ?? ''))
const parentPath = computed(() => parent.value ? entityPath('abteilungen', parent.value.id ?? '', parent.value.name ?? '') : '')
const fullName = computed(() => organizationPath(department.value.clubName, parent.value?.name, department.value.name))
const addressLines = computed(() => [department.value.street, [department.value.postalCode, department.value.city].filter(Boolean).join(' ')].filter(Boolean))
const website = computed(() => safeExternalUrl(department.value.websiteUrl))
// An inherited email is revealed through the endpoint of the club or department it belongs to.
const emailSource = computed(() => department.value.emailSource ? contactDetailsPath(department.value.emailSource) : null)
const emailOwner = computed(() => department.value.emailSource?.name ?? department.value.name ?? '')
// Only own links identify this department or group; inherited ones belong to the club or department.
const sameAs = computed(() => [
  department.value.websiteSource ? null : website.value,
  ...(department.value.socialMediaSource ? [] : department.value.socialMediaLinks ?? []).map(link => safeExternalUrl(link.url))
].filter((url): url is string => !!url))
const groups = computed(() => department.value.groups ?? [])
// Values taken over from the department or club, summarised once below the contact box.
const inheritedLines = computed(() => inheritedSummary([
  ...(addressLines.value.length ? [{ label: 'Adresse', source: department.value.addressSource }] : []),
  ...(department.value.hasEmail ? [{ label: 'E-Mail', source: department.value.emailSource }] : []),
  ...(website.value ? [{ label: 'Website', source: department.value.websiteSource }] : []),
  ...(department.value.socialMediaLinks?.length ? [{ label: 'Social Media', source: department.value.socialMediaSource }] : [])
]))

useSeoMeta({
  title: () => `${department.value.name} – ${organizationPath(parent.value?.name, department.value.clubName)}`,
  description: () => firstLine(department.value.description)
    ?? `${fullName.value}: ${isGroup.value ? 'Infos, ' : ''}Ansprechpersonen und Termine.`,
  ogUrl: canonical,
  ogImage: () => department.value.logoUrl ? new URL(department.value.logoUrl, config.public.siteUrl).toString() : undefined
})
useHead(() => ({
  script: [{
    type: 'application/ld+json',
    innerHTML: JSON.stringify({
      '@context': 'https://schema.org',
      '@type': isGroup.value ? 'SportsTeam' : 'SportsOrganization',
      name: department.value.name,
      url: canonical.value,
      description: department.value.description ?? undefined,
      sameAs: sameAs.value.length ? sameAs.value : undefined,
      parentOrganization: isGroup.value
        ? {
            '@type': 'SportsOrganization',
            name: parent.value?.name,
            url: new URL(parentPath.value, config.public.siteUrl).toString(),
            parentOrganization: { '@type': 'SportsOrganization', name: department.value.clubName, url: new URL(clubPath.value, config.public.siteUrl).toString() }
          }
        : { '@type': 'SportsOrganization', name: department.value.clubName, url: new URL(clubPath.value, config.public.siteUrl).toString() }
    })
  }]
}))
</script>

<template>
  <div class="container page">
    <nav class="breadcrumbs" aria-label="Brotkrumen">
      <ol>
        <li><NuxtLink to="/vereine">Vereine</NuxtLink></li>
        <li><NuxtLink :to="clubPath">{{ department.clubName }}</NuxtLink></li>
        <li v-if="parent"><NuxtLink :to="parentPath">{{ parent.name }}</NuxtLink></li>
        <li><span aria-current="page">{{ department.name }}</span></li>
      </ol>
    </nav>

    <div class="detail-head">
      <span class="logo-box large" :class="{ initial: !department.logoUrl }" aria-hidden="true">
        <img v-if="department.logoUrl" :src="department.logoUrl" alt="" width="96" height="96">
        <template v-else>{{ department.name?.charAt(0) }}</template>
      </span>
      <div>
        <p class="kicker" data-testid="organization-hierarchy">
          <template v-if="parent">
            Gruppe der Abteilung <NuxtLink :to="parentPath">{{ parent.name }}</NuxtLink> · <NuxtLink :to="clubPath">{{ department.clubName }}</NuxtLink>
          </template>
          <template v-else>Abteilung von <NuxtLink :to="clubPath">{{ department.clubName }}</NuxtLink></template>
        </p>
        <h1>{{ department.name }}</h1>
      </div>
    </div>

    <p v-if="department.description" class="prose description" style="margin-top:1.5rem">{{ department.description }}</p>

    <dl v-if="addressLines.length || department.hasEmail || website || department.socialMediaLinks?.length" class="facts">
      <div v-if="addressLines.length">
        <AppIcon name="pin" /><dt>Adresse</dt>
        <dd>{{ addressLines.join(', ') }}</dd>
      </div>
      <div v-if="department.hasEmail">
        <AppIcon name="mail" /><dt>E-Mail</dt>
        <dd>
          <ContactReveal :source="emailSource ?? `/api/public/v1/departments/${id}/contact-details`" has-email :owner="emailOwner" />
        </dd>
      </div>
      <div v-if="website">
        <AppIcon name="globe" /><dt>Website</dt>
        <dd>
          <a :href="website" rel="noopener noreferrer" target="_blank">{{ displayHost(website) }}</a>
        </dd>
      </div>
      <div v-if="department.socialMediaLinks?.length">
        <AppIcon name="share" /><dt>Social Media</dt>
        <dd>
          <SocialLinks :links="department.socialMediaLinks" :name="department.socialMediaSource?.name ?? department.name" />
        </dd>
      </div>
      <div v-if="inheritedLines.length" class="facts-origin" data-testid="inherited-note">
        <dt>Herkunft der Angaben</dt>
        <dd>
          <span v-for="line in inheritedLines" :key="line.to">{{ line.text }} <NuxtLink :to="line.to">{{ line.name }}</NuxtLink></span>
        </dd>
      </div>
    </dl>

    <section v-if="groups.length" class="section" style="margin-top:2.5rem" aria-labelledby="groups-heading">
      <div class="section-head"><h2 id="groups-heading">Gruppen</h2></div>
      <ul class="list two-col">
        <li v-for="group in groups" :key="group.id">
          <NuxtLink :to="entityPath('abteilungen', group.id || '', group.name || '')" class="row">
            <span class="row-body">
              <span class="row-title">{{ group.name }}</span>
              <span v-if="firstLine(group.description)" class="row-meta">{{ firstLine(group.description) }}</span>
            </span>
          </NuxtLink>
        </li>
      </ul>
    </section>

    <section class="section" style="margin-top:2.5rem" aria-labelledby="contacts-heading">
      <div class="section-head"><h2 id="contacts-heading">Ansprechpersonen</h2></div>
      <ContactList :contacts="department.contacts" />
    </section>

    <section v-if="events?.items?.length" class="section" aria-labelledby="department-events-heading">
      <div class="section-head"><h2 id="department-events-heading">Nächste Termine</h2></div>
      <ul class="list">
        <EventRow v-for="item in events.items" :key="item.id" :event="item" />
      </ul>
    </section>
  </div>
</template>
