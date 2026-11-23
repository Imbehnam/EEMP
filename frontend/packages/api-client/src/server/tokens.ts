import type { NextResponse } from 'next/server';

import { ApiError, ErrorCodes, toApiError } from '../problem';
import type { TokenResponse } from '../types';
import { cookieNames } from '../cookies';
import type { BffConfig } from './config';

/** Refresh slightly before the backend's expiry to absorb clock skew and request latency. */
export const ACCESS_SKEW_SECONDS = 30;

export interface CookieReader {
  get(name: string): { value: string } | undefined;
}

export function readTokens(config: BffConfig, cookies: CookieReader) {
  const names = cookieNames(config.appId);
  return {
    accessToken: cookies.get(names.access)?.value || undefined,
    refreshToken: cookies.get(names.refresh)?.value || undefined,
  };
}

export function setSessionCookies(config: BffConfig, response: NextResponse, tokens: TokenResponse, now = Date.now()) {
  const names = cookieNames(config.appId);
  const expiresAt = Date.parse(tokens.accessTokenExpiresAt);
  const accessMaxAge = Number.isFinite(expiresAt)
    ? Math.max(1, Math.floor((expiresAt - now) / 1000) - ACCESS_SKEW_SECONDS)
    : 60;
  const base = { httpOnly: true, secure: config.cookieSecure, sameSite: 'lax' as const, path: '/' };
  response.cookies.set(names.access, tokens.accessToken, { ...base, maxAge: accessMaxAge });
  response.cookies.set(names.refresh, tokens.refreshToken, { ...base, maxAge: config.refreshMaxAgeSeconds });
}

export function clearSessionCookies(config: BffConfig, response: NextResponse) {
  const names = cookieNames(config.appId);
  for (const name of [names.access, names.refresh]) {
    response.cookies.set(name, '', { httpOnly: true, secure: config.cookieSecure, sameSite: 'lax', path: '/', maxAge: 0 });
  }
}

export type RefreshResult = { ok: true; tokens: TokenResponse } | { ok: false; error: ApiError };

const inflight = new Map<string, { promise: Promise<RefreshResult>; settledAt?: number }>();
const REUSE_WINDOW_MS = 10_000;

/**
 * Single-flight refresh. Refresh tokens rotate (single use), so concurrent requests holding the
 * same refresh token must share one backend call; the result is reused briefly for stragglers.
 */
export function refreshTokens(config: BffConfig, refreshToken: string): Promise<RefreshResult> {
  const now = Date.now();
  for (const [key, entry] of inflight) {
    if (entry.settledAt && now - entry.settledAt > REUSE_WINDOW_MS) inflight.delete(key);
  }
  const key = `${config.appId}:${refreshToken}`;
  const existing = inflight.get(key);
  if (existing) return existing.promise;

  const entry: { promise: Promise<RefreshResult>; settledAt?: number } = {
    promise: (async (): Promise<RefreshResult> => {
      const response = await config.fetch(`${config.apiBaseUrl}/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ refreshToken }),
        cache: 'no-store',
      });
      if (!response.ok) return { ok: false, error: await toApiError(response) };
      return { ok: true, tokens: (await response.json()) as TokenResponse };
    })()
      .catch((): RefreshResult => ({ ok: false, error: new ApiError(0, { code: ErrorCodes.network }) }))
      .finally(() => {
        entry.settledAt = Date.now();
      }),
  };
  inflight.set(key, entry);
  return entry.promise;
}
