import { defineConfig, devices } from '@playwright/test'

/**
 * Generates the screenshots of the user documentation (user-docs/assets/images/screenshots)
 * against the Vite dev server with the mocked API of the UI tests: `npm run docs:screenshots`.
 * `--update-snapshots=changed` rewrites an image only if it differs visibly, so re-runs do not churn Git.
 */
export default defineConfig({
  testDir: './tests/docs',
  fullyParallel: true,
  reporter: 'list',
  snapshotPathTemplate: '../user-docs/assets/images/screenshots/{arg}{ext}',
  use: {
    ...devices['Desktop Chrome'],
    baseURL: 'http://localhost:5173', locale: 'de-DE', timezoneId: 'Europe/Berlin',
    viewport: { width: 1280, height: 800 }, colorScheme: 'light', reducedMotion: 'reduce'
  },
  webServer: { command: 'npm run dev', url: 'http://localhost:5173', reuseExistingServer: true }
})
