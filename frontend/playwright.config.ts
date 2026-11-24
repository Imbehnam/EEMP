import { defineConfig, devices } from '@playwright/test';

/**
 * Smoke tests against production builds in mock mode (no .NET API needed).
 * Run `pnpm build` first; CI does. Servers use the in-process mock backend.
 */
const mockEnv = 'EEMP_API_MOCK=true EEMP_ALLOW_MOCK_IN_PRODUCTION=true EEMP_COOKIE_SECURE=false NEXT_TELEMETRY_DISABLED=1';

export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: { trace: 'retain-on-failure', ...devices['Desktop Chrome'] },
  projects: [{ name: 'chromium' }],
  webServer: [
    {
      command: `${mockEnv} pnpm --filter @eemp/admin exec next start --port 3001`,
      port: 3001,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
    {
      command: `${mockEnv} pnpm --filter @eemp/web exec next start --port 3000`,
      port: 3000,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
});
