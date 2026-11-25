import { API, PASSWORDS } from './helpers';

/** Fails fast with a clear message when the real API is not running or its Development seed is missing. */
export default async function globalSetup() {
  const response = await fetch(`${API}/health/live`).catch(() => null);
  if (!response?.ok) {
    throw new Error(`EEMP API is not reachable at ${API}/health/live. Start it first (see docs/09-quality/M1_INTEGRATION_REPORT.md).`);
  }
  const login = await fetch(`${API}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tenantSlug: 'demo', userName: 'student.one', password: PASSWORDS.demoUser }),
  }).catch(() => null);
  if (login?.status !== 200) {
    throw new Error(
      `Seeded user demo/student.one cannot sign in (HTTP ${login?.status ?? 'unreachable'}). The API must run with the ` +
        'Development seed: Seed:Enabled=true (appsettings.Development.json is read from the content root, so start the API ' +
        'from its output directory or set Seed__Enabled=true) and Seed__*Password matching EEMP_E2E_*_PASSWORD.',
    );
  }
}
