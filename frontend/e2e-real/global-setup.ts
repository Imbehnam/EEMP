import { API } from './helpers';

/** Fails fast with a clear message when the real API is not running. */
export default async function globalSetup() {
  const response = await fetch(`${API}/health/live`).catch(() => null);
  if (!response?.ok) {
    throw new Error(`EEMP API is not reachable at ${API}/health/live. Start it first (see docs/09-quality/M1_INTEGRATION_REPORT.md).`);
  }
}
