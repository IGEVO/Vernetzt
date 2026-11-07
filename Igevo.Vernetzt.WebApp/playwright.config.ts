import { defineConfig, devices } from '@playwright/test'

/**
 * UI tests against the Vite dev server with a mocked API (see tests/e2e/support).
 * Feature specs run in Chromium; the cross-browser and mobile projects run the smoke and responsive specs.
 * Tests against a real backend: playwright.integration.config.ts (`npm run test:integration`).
 */
const crossBrowser = /(smoke|responsive)\.spec\.ts/

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: { baseURL: 'http://localhost:5173', trace: 'on-first-retry', locale: 'de-DE', timezoneId: 'Europe/Berlin' },
  webServer: { command: 'npm run dev', url: 'http://localhost:5173', reuseExistingServer: !process.env.CI },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] }, testMatch: crossBrowser },
    { name: 'webkit', use: { ...devices['Desktop Safari'] }, testMatch: crossBrowser },
    { name: 'mobile-chrome', use: { ...devices['Pixel 7'] }, testMatch: crossBrowser },
    { name: 'mobile-safari', use: { ...devices['iPhone 15'] }, testMatch: crossBrowser }
  ]
})
