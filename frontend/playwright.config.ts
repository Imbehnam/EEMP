import { defineConfig, devices } from '@playwright/test';

import { chromiumLaunchOptions } from './e2e/browser';

/**
 * Smoke tests against production builds in mock mode (no .NET API needed).
 * Run `pnpm build` first; CI does. Servers use the in-process mock backend.
 */
const mockEnv = 'EEMP_API_MOCK=true EEMP_ALLOW_MOCK_IN_PRODUCTION=true EEMP_COOKIE_SECURE=false NEXT_TELEMETRY_DISABLED=1';

/** Ports are overridable so several checkouts can run side by side (EEMP_E2E_ADMIN_PORT / EEMP_E2E_WEB_PORT). */
const adminPort = Number(process.env.EEMP_E2E_ADMIN_PORT ?? 3001);
const webPort = Number(process.env.EEMP_E2E_WEB_PORT ?? 3000);

export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: { trace: 'retain-on-failure', ...devices['Desktop Chrome'], launchOptions: chromiumLaunchOptions() },
  projects: [{ name: 'chromium' }],
  webServer: [
    {
      command: `${mockEnv} pnpm --filter @eemp/admin exec next start --port ${adminPort}`,
      port: adminPort,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
    {
      command: `${mockEnv} pnpm --filter @eemp/web exec next start --port ${webPort}`,
      port: webPort,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
});
