import type { NextRequest } from 'next/server';

import { CSRF_HEADER } from '../csrf-header';
import { cookieNames } from '../cookies';
import type { BffConfig } from './config';

export function newCsrfToken(): string {
  const bytes = globalThis.crypto.getRandomValues(new Uint8Array(32));
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

function requestHost(request: NextRequest): string | null {
  return request.headers.get('x-forwarded-host') ?? request.headers.get('host');
}

/**
 * Mutating BFF requests must (1) come from our own origin when the browser sends Origin and
 * (2) carry the double-submit CSRF token: header X-CSRF-Token equal to the CSRF cookie.
 * Session cookies are also SameSite=Lax as defence in depth.
 */
export function verifyCsrf(config: BffConfig, request: NextRequest): boolean {
  const method = request.method.toUpperCase();
  if (method === 'GET' || method === 'HEAD' || method === 'OPTIONS') return true;

  const origin = request.headers.get('origin');
  if (origin) {
    let originHost: string;
    try {
      originHost = new URL(origin).host;
    } catch {
      return false;
    }
    if (originHost !== requestHost(request)) return false;
  }
  const cookie = request.cookies.get(cookieNames(config.appId).csrf)?.value ?? '';
  const header = request.headers.get(CSRF_HEADER) ?? '';
  return cookie.length >= 32 && timingSafeEqual(cookie, header);
}
