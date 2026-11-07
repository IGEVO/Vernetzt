import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests against the real backend.
 *
 * Playwright starts its own ASP.NET server in the "Test" environment (fresh temporary SQLite database, SystemAdmin
 * admin@test.local, no development seed data) plus a Vite dev server proxying to it – your development data is
 * never touched. E-mails go to Mailpit and are read through its HTTP API.
 *
 * Prerequisite: a running Mailpit (e.g. started by the Aspire AppHost, or
 * `docker run -p 56025:1025 -p 56080:8025 axllent/mailpit`). Override with MAILPIT_URL / MAILPIT_SMTP_PORT.
 */
export const ports = { api: Number(process.env.IT_API_PORT ?? 5242), web: Number(process.env.IT_WEB_PORT ?? 5174) }
export const baseURL = `http://localhost:${ports.web}`
const smtpPort = process.env.MAILPIT_SMTP_PORT ?? '56025'
// CI builds the solution in Release beforehand: IT_DOTNET_CONFIGURATION=Release reuses that build (--no-build).
const dotnetArgs = process.env.IT_DOTNET_CONFIGURATION ? ` -c ${process.env.IT_DOTNET_CONFIGURATION} --no-build` : ''

export default defineConfig({
  testDir: './tests/integration',
  globalSetup: './tests/integration/support/globalSetup.ts',
  // The flows build on each other and share one server/database.
  workers: 1,
  fullyParallel: false,
  retries: 0,
  timeout: 60_000,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never', outputFolder: 'playwright-report/integration' }]] : 'list',
  use: { ...devices['Desktop Chrome'], baseURL, locale: 'de-DE', timezoneId: 'Europe/Berlin', trace: 'retain-on-failure' },
  projects: [{ name: 'integration-chromium' }],
  webServer: [
    {
      command: `dotnet run --project ../Igevo.Vernetzt.Server --no-launch-profile${dotnetArgs}`,
      url: `http://localhost:${ports.api}/api/version`,
      timeout: 180_000,
      // Always a fresh server (the tests rely on an empty database); IT_REUSE=1 reuses running servers for debugging.
      reuseExistingServer: !!process.env.IT_REUSE,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        ASPNETCORE_ENVIRONMENT: 'Test',
        ASPNETCORE_URLS: `http://localhost:${ports.api}`,
        App__BaseUrl: baseURL,
        Auth__Passkeys__RelyingPartyId: 'localhost',
        Auth__Passkeys__AllowedOrigins__0: baseURL,
        // All test traffic comes from one IP; the production limits (10 / 20 per minute) would throttle the flow.
        RateLimiting__PublicAuth__PermitLimit: '200',
        RateLimiting__PublicTokenFlow__PermitLimit: '200',
        Email__Smtp__Host: 'localhost',
        Email__Smtp__Port: smtpPort,
        Email__Smtp__UseSsl: 'false',
        Email__Smtp__Username: '',
        Email__Smtp__Password: ''
      }
    },
    {
      command: 'npm run dev',
      url: baseURL,
      timeout: 60_000,
      reuseExistingServer: !!process.env.IT_REUSE,
      env: { PORT: String(ports.web), VERNETZT_API_URL: `http://localhost:${ports.api}` }
    }
  ]
})
