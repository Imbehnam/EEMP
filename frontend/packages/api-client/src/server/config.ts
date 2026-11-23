import 'server-only';

import { getSharedMockBackend, MOCK_API_BASE_URL } from '../mock';

export interface BffConfig {
  /** Short app id used to namespace cookies ("admin" | "web") so both apps can share a host. */
  appId: string;
  apiBaseUrl: string;
  mock: boolean;
  cookieSecure: boolean;
  refreshMaxAgeSeconds: number;
  fetch: typeof fetch;
}

export { cookieNames, type CookieNames } from '../cookies';

/** Reads server-only environment. EEMP_API_BASE_URL is never exposed to the browser bundle. */
export function getBffConfig(appId: string, env: NodeJS.ProcessEnv = process.env): BffConfig {
  const mock = env.EEMP_API_MOCK === 'true' || env.EEMP_API_MOCK === '1';
  const apiBaseUrl = mock ? MOCK_API_BASE_URL : (env.EEMP_API_BASE_URL ?? 'http://localhost:5080/api/v1');
  if (mock && env.NODE_ENV === 'production' && env.EEMP_ALLOW_MOCK_IN_PRODUCTION !== 'true') {
    // `next start` used by e2e tests sets the explicit opt-in. Real deployments never should.
    throw new Error('EEMP_API_MOCK is enabled in a production build without EEMP_ALLOW_MOCK_IN_PRODUCTION=true');
  }
  return {
    appId,
    apiBaseUrl: apiBaseUrl.replace(/\/+$/, ''),
    mock,
    cookieSecure: env.EEMP_COOKIE_SECURE === 'true' || (env.NODE_ENV === 'production' && env.EEMP_COOKIE_SECURE !== 'false'),
    refreshMaxAgeSeconds: Number(env.EEMP_REFRESH_COOKIE_MAX_AGE ?? '') || 14 * 24 * 60 * 60,
    fetch: mock ? getSharedMockBackend().fetch : (input, init) => globalThis.fetch(input, init),
  };
}
