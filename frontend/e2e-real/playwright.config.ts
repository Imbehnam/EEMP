import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end suite against the REAL .NET API (no mock). Not part of the default `pnpm test:e2e`
 * (that config only reads ./e2e). Prerequisites, see docs/09-quality/M1_INTEGRATION_REPORT.md:
 *   - API on EEMP_E2E_API (default http://127.0.0.1:5080) with the Development seed and
 *     Jwt__AccessTokenMinutes=1 so the refresh-rotation spec does not wait 15 minutes.
 *   - `pnpm build` done; the web servers below are started with EEMP_API_MOCK=false.
 * Specs share one database, so they run serially and create uniquely named records.
 */
const api = process.env.EEMP_E2E_API ?? 'http://127.0.0.1:5080';
const realEnv = `EEMP_API_MOCK=false EEMP_API_BASE_URL=${api}/api/v1 EEMP_COOKIE_SECURE=false NEXT_TELEMETRY_DISABLED=1`;

/** Ports are overridable so several checkouts can run side by side (EEMP_E2E_ADMIN_PORT / EEMP_E2E_WEB_PORT). */
const adminPort = Number(process.env.EEMP_E2E_ADMIN_PORT ?? 3001);
const webPort = Number(process.env.EEMP_E2E_WEB_PORT ?? 3000);

export default defineConfig({
  testDir: '.',
  testMatch: /.*\.real\.spec\.ts$/,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never', outputFolder: '../playwright-report-real' }]] : 'list',
  outputDir: '../test-results-real',
  globalSetup: './global-setup.ts',
  use: { trace: 'retain-on-failure', actionTimeout: 15_000, navigationTimeout: 30_000, ...devices['Desktop Chrome'] },
  projects: [{ name: 'real-api' }],
  webServer: [
    {
      command: `${realEnv} pnpm --filter @eemp/admin exec next start --port ${adminPort}`,
      port: adminPort,
      reuseExistingServer: true,
      timeout: 60_000,
    },
    {
      command: `${realEnv} pnpm --filter @eemp/web exec next start --port ${webPort}`,
      port: webPort,
      reuseExistingServer: true,
      timeout: 60_000,
    },
  ],
});
