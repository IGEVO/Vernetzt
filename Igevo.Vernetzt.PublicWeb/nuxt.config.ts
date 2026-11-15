// Nuxt renders its payload and the theme bootstrap as inline scripts, so script-src needs
// 'unsafe-inline'. Everything else is locked to this origin.
const contentSecurityPolicy = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data:",
  "font-src 'self'",
  "connect-src 'self'",
  "object-src 'none'",
  "base-uri 'self'",
  "frame-ancestors 'none'",
  "form-action 'self'"
].join('; ')

export default defineNuxtConfig({
  compatibilityDate: '2026-11-01',
  devtools: { enabled: false },
  css: ['~/assets/styles/main.css'],
  runtimeConfig: {
    apiBaseUrl: 'http://localhost:5000',
    // Shared key the API requires for /api/public (PublicApi:AccessKey). Server-only, never public.
    apiAccessKey: '',
    // Number of trusted reverse proxies in front of the portal (0 = use the socket address).
    trustedProxyHops: 0,
    public: {
      siteUrl: 'http://localhost:3000',
      siteName: 'Vernetzt',
      // Optional: link to the Vernetzt app for club members (footer).
      // Texts (title, imprint, privacy, provider, logo) are maintained in the app's system settings.
      appUrl: ''
    }
  },
  app: {
    head: {
      htmlAttrs: { lang: 'de' },
      meta: [
        { name: 'viewport', content: 'width=device-width, initial-scale=1, viewport-fit=cover' },
        { name: 'theme-color', content: '#0D4A70', media: '(prefers-color-scheme: light)' },
        { name: 'theme-color', content: '#121212', media: '(prefers-color-scheme: dark)' },
        { name: 'color-scheme', content: 'light dark' },
        { name: 'format-detection', content: 'telephone=no' }
      ],
      link: [
        { rel: 'icon', href: '/favicon.ico', sizes: 'any' },
        { rel: 'icon', href: '/logo.svg', type: 'image/svg+xml' }
      ]
    }
  },
  typescript: {
    strict: true,
    typeCheck: true
  },
  nitro: {
    compressPublicAssets: true
  },
  routeRules: {
    '/_nuxt/**': { headers: { 'cache-control': 'public, max-age=31536000, immutable' } }
  },
  $production: {
    routeRules: {
      '/**': {
        headers: {
          'content-security-policy': contentSecurityPolicy,
          'x-content-type-options': 'nosniff',
          'x-frame-options': 'DENY',
          'referrer-policy': 'strict-origin-when-cross-origin',
          'permissions-policy': 'camera=(), microphone=(), geolocation=(), interest-cohort=()'
        }
      }
    }
  }
})
