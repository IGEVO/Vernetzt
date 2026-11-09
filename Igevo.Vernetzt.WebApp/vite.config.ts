import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'

const apiTarget = process.env.VERNETZT_API_URL ?? 'https://localhost:5001'

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
      registerType: 'prompt',
      includeAssets: ['favicon.ico', 'images/*.svg', 'images/*.png'],
      manifest: {
        name: 'Vernetzt',
        short_name: 'Vernetzt',
        description: 'Vereine, Abteilungen und Kontakte vernetzen',
        theme_color: '#0D4A70',
        background_color: '#F5F5F5',
        display: 'standalone',
        start_url: '/',
        icons: [
          { src: '/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icon-512.png', sizes: '512x512', type: 'image/png' }
        ]
      },
      injectManifest: {
        globPatterns: ['**/*.{js,css,svg,png,ico,woff2}', 'offline.html']
      }
    })
  ],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  publicDir: 'wwwroot',
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true, secure: false },
      // Legacy /help links are redirected to the public user documentation by the server
      '/help': { target: apiTarget, changeOrigin: true, secure: false },
      '/health': { target: apiTarget, changeOrigin: true, secure: false },
      // CardDAV address book (PROPFIND/REPORT are forwarded like any other method)
      '/carddav': { target: apiTarget, changeOrigin: true, secure: false },
      '/.well-known/carddav': { target: apiTarget, changeOrigin: true, secure: false }
    }
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: true,
    chunkSizeWarningLimit: 600
  }
})
