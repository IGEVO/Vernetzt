// docmd.config.js
// DOCS_BASE: URL path the site is served from. Default /help/ (shipped inside the web app);
// the GitHub Pages build uses /Vernetzt/.
const base = process.env.DOCS_BASE || '/help/';

export default defineConfig({
  title: 'Vernetzt - Dokumentation',
  url: '',
  base,

  logo: {
    light: 'assets/images/logo.png',
    dark: 'assets/images/logo.png',
    alt: 'Vernetzt',
    href: base + 'index.html'
  },
  favicon: 'assets/favicon.ico',

  src: 'docs',
  out: 'site',

  layout: {
    spa: true,
    header: {
      enabled: true
    },
    sidebar: {
      collapsible: true,
      defaultCollapsed: false
    },
    optionsMenu: {
      position: 'sidebar-top',
      components: {
        search: true,
        themeSwitch: true,
        sponsor: null
      }
    },
    footer: {
      style: 'minimal',
      content: '© ' + new Date().getFullYear() + ' Vernetzt',
      branding: false
    }
  },

  theme: {
    name: 'default',
    appearance: 'system',
    codeHighlight: true,
    customCss: []
  },

  minify: true,
  autoTitleFromH1: true,
  copyCode: true,
  pageNavigation: true,
  customJs: [],

  navigation: [
    { title: 'Start', path: '/', icon: 'house' },
    {
      title: 'Anmeldung & Zugang',
      icon: 'log-in',
      collapsible: true,
      children: [
        { title: 'Anmeldung per E-Mail und Passkey', path: '/anmeldung/login-und-passkey', icon: 'key' },
        { title: 'Einladung und Registrierung', path: '/anmeldung/einladung-und-registrierung', icon: 'user-plus' }
      ]
    },
    {
      title: 'Vernetzt nutzen',
      icon: 'layout-dashboard',
      collapsible: true,
      children: [
        { title: 'Navigation und Bedienung', path: '/app/navigation-und-bedienung', icon: 'compass' },
        { title: 'Dashboard', path: '/app/dashboard', icon: 'layout-dashboard' },
        { title: 'Vereine: Übersicht', path: '/app/vereine-uebersicht', icon: 'building' },
        { title: 'Vereinsdetails', path: '/app/vereinsdetails', icon: 'building' },
        { title: 'Abteilungsdetails', path: '/app/abteilungsdetails', icon: 'folder' },
        { title: 'Gruppen', path: '/app/gruppen', icon: 'users-round' },
        { title: 'Kontakte', path: '/app/kontakte', icon: 'users' },
        { title: 'Hauptkalender', path: '/app/hauptkalender', icon: 'calendar' },
        { title: 'E-Mail versenden', path: '/app/email-versenden', icon: 'send' },
        { title: 'Mein Profil', path: '/app/mein-profil', icon: 'user-round' },
        { title: 'Persönliche Adresse und Amtsadresse', path: '/app/amtsadressen', icon: 'briefcase' },
        { title: 'Persönliche Einstellungen', path: '/app/persoenliche-einstellungen', icon: 'settings' },
        { title: 'Adressbuch abonnieren', path: '/app/adressbuch', icon: 'contact' }
      ]
    },
    {
      title: 'Administration',
      icon: 'shield',
      collapsible: true,
      children: [
        { title: 'Benutzerverwaltung', path: '/admin/benutzerverwaltung', icon: 'user-cog' },
        { title: 'Vereinstypen', path: '/admin/vereinstypen', icon: 'tags' },
        { title: 'Kontaktpositionen', path: '/admin/kontaktpositionen', icon: 'tags' },
        { title: 'Ferien & Feiertage', path: '/admin/ferien-und-feiertage', icon: 'calendar-days' },
        { title: 'Einstellungen', path: '/admin/einstellungen', icon: 'settings-2' },
        { title: 'Audit-Log', path: '/admin/audit-log', icon: 'history' }
      ]
    },
    {
      title: 'Datenschutz',
      icon: 'lock',
      collapsible: true,
      children: [
        { title: 'Datenverarbeitung und Sichtbarkeit', path: '/datenschutz/datenverarbeitung', icon: 'lock' }
      ]
    },
    { title: 'FAQ', path: '/faq', icon: 'info' }
  ],

  plugins: {
    search: {},
    mermaid: {}
  }
  
});
