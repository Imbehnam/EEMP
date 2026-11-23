import 'server-only';

import { cookies } from 'next/headers';
import { cache } from 'react';

import { createApiClient } from '../client';
import { createEndpoints } from '../endpoints';
import { isApiError } from '../problem';
import type { CurrentUser } from '../types';
import { getBffConfig } from './config';
import { readTokens } from './tokens';

export type ServerSession =
  | { status: 'authenticated'; user: CurrentUser }
  /** Access token missing/expired but a refresh token exists: redirect to /api/auth/refresh. */
  | { status: 'needs-refresh' }
  | { status: 'anonymous' };

/**
 * Resolves the signed-in user for a server component. Server components cannot set cookies,
 * so expiry is surfaced as `needs-refresh` and the caller redirects through the refresh route.
 * This is a UX gate only — the backend authorizes every API call independently.
 */
export const getServerSession = cache(async (appId: string, locale: string): Promise<ServerSession> => {
  const cfg = getBffConfig(appId);
  const { accessToken, refreshToken } = readTokens(cfg, await cookies());
  if (!accessToken) return refreshToken ? { status: 'needs-refresh' } : { status: 'anonymous' };
  try {
    const user = await createServerEndpoints(appId, accessToken, locale).auth.me();
    return { status: 'authenticated', user };
  } catch (error) {
    if (isApiError(error) && error.status === 401) {
      return refreshToken ? { status: 'needs-refresh' } : { status: 'anonymous' };
    }
    throw error;
  }
});

/** Server-side typed endpoints for RSC data loading with the caller's access token. */
export function createServerEndpoints(appId: string, accessToken: string, locale: string) {
  const cfg = getBffConfig(appId);
  return createEndpoints(
    createApiClient({
      baseUrl: cfg.apiBaseUrl,
      fetch: cfg.fetch,
      headers: () => ({ Authorization: `Bearer ${accessToken}`, 'Accept-Language': locale }),
    }),
  );
}
