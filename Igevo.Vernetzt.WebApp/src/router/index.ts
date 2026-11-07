import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { PASSKEY_REQUIRED_PATH, useAuthStore } from '@/stores/auth'

declare module 'vue-router' {
  interface RouteMeta { requiresAuth?: boolean; roles?: string[]; title?: string }
}

const routes: RouteRecordRaw[] = [
  {
    path: '/', component: () => import('@/layouts/MainLayout.vue'), meta: { requiresAuth: true }, children: [
      { path: '', component: () => import('@/views/DashboardView.vue'), meta: { title: 'Dashboard' } },
      { path: 'clubs', alias: '/vereine', component: () => import('@/views/clubs/ClubListView.vue'), meta: { title: 'Vereine' } },
      { path: 'clubs/:id', component: () => import('@/views/clubs/ClubDetailView.vue'), meta: { title: 'Verein' } },
      { path: 'clubs/:id/events', redirect: (to) => ({ path: `/clubs/${String(to.params.id)}`, query: { tab: 'events' } }) },
      { path: 'departments/:id', component: () => import('@/views/departments/DepartmentDetailView.vue'), meta: { title: 'Abteilung oder Gruppe' } },
      { path: 'departments/:id/events', redirect: (to) => ({ path: `/departments/${String(to.params.id)}`, query: { tab: 'events' } }) },
      { path: 'kontakte', alias: '/contacts', component: () => import('@/views/contacts/ContactsView.vue'), meta: { title: 'Kontakte' } },
      { path: 'konto', component: () => import('@/views/AccountView.vue'), meta: { title: 'Mein Konto' } },
      { path: 'profil', alias: '/profile', redirect: '/konto' },
      { path: 'willkommen', component: () => import('@/views/WelcomeView.vue'), meta: { title: 'Willkommen' } },
      { path: 'einstellungen', alias: '/settings', redirect: { path: '/konto', query: { tab: 'sicherheit' } } },
      { path: 'calendar/main', component: () => import('@/views/events/CalendarView.vue'), meta: { title: 'Kalender' } },
      { path: 'emails/send', component: () => import('@/views/email/SendEmailView.vue'), meta: { title: 'E-Mail versenden' } },
      { path: 'admin/club-types', component: () => import('@/views/admin/NamedEntitiesView.vue'), props: { kind: 'club-types' }, meta: { title: 'Vereinstypen', roles: ['SystemAdmin', 'Manager'] } },
      { path: 'admin/contact-positions', component: () => import('@/views/admin/NamedEntitiesView.vue'), props: { kind: 'contact-positions' }, meta: { title: 'Kontaktpositionen', roles: ['SystemAdmin', 'Manager'] } },
      { path: 'admin/holidays', component: () => import('@/views/admin/HolidaysView.vue'), meta: { title: 'Ferien & Feiertage', roles: ['SystemAdmin', 'Manager'] } },
      { path: 'admin/users', component: () => import('@/views/admin/UsersView.vue'), meta: { title: 'Benutzerverwaltung', roles: ['SystemAdmin', 'Manager'] } },
      { path: 'admin/audit-log', component: () => import('@/views/admin/AuditLogView.vue'), meta: { title: 'Audit-Log', roles: ['SystemAdmin'] } },
      { path: 'admin/settings', component: () => import('@/views/admin/AdminSettingsView.vue'), meta: { title: 'Systemeinstellungen', roles: ['SystemAdmin'] } }
    ]
  },
  {
    path: '/', component: () => import('@/layouts/PublicLayout.vue'), children: [
      { path: 'login', component: () => import('@/views/LoginView.vue'), meta: { title: 'Anmelden' } },
      { path: 'onboarding', component: () => import('@/views/OnboardingView.vue'), meta: { title: 'Einladung annehmen' } },
      { path: 'passkey-erforderlich', component: () => import('@/views/PasskeyRequiredView.vue'), meta: { title: 'Anmeldung mit Passkey' } },
      { path: 'forbidden', component: () => import('@/views/ForbiddenView.vue'), meta: { title: 'Zugriff verweigert' } },
      { path: ':pathMatch(.*)*', component: () => import('@/views/NotFoundView.vue'), meta: { title: 'Seite nicht gefunden' } }
    ]
  }
]

const router = createRouter({ history: createWebHistory(), routes, scrollBehavior: () => ({ top: 0 }) })

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (to.matched.some((route) => route.meta.requiresAuth)) {
    await auth.initialize()
    if (!auth.isAuthenticated) return { path: '/login', query: { returnUrl: to.fullPath } }
    // Admins signed in with a login link must first sign in with a passkey (enforced by the server as well).
    if (auth.passkeyRequired) return { path: PASSKEY_REQUIRED_PATH, query: { returnUrl: to.fullPath } }
  }
  const roles = to.matched.flatMap((route) => route.meta.roles ?? [])
  if (roles.length && !auth.hasRole(...roles)) return '/forbidden'
  if (to.path === '/login' && auth.initialized && auth.isAuthenticated && !to.query.token) return '/'
})

router.afterEach((to) => {
  document.title = `${to.meta.title ?? 'Vernetzt'} - Vernetzt`
  requestAnimationFrame(() => (document.querySelector('h1') as HTMLElement | null)?.focus())
})

export default router
