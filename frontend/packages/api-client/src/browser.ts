import { createApiClient, type ApiClient } from './client';
import { cookieNames } from './cookies';
import { CSRF_HEADER } from './csrf-header';
import { createEndpoints } from './endpoints';

export const BFF_BASE_PATH = '/api/bff';
export { CSRF_HEADER } from './csrf-header';

export function readCookie(name: string, cookieString: string = typeof document === 'undefined' ? '' : document.cookie): string | undefined {
  for (const part of cookieString.split(';')) {
    const [key, ...rest] = part.trim().split('=');
    if (key === name) return decodeURIComponent(rest.join('='));
  }
  return undefined;
}

export interface BrowserClientOptions {
  /** App id used to namespace cookies ("admin" | "web"). */
  appId: string;
  locale: string;
  /** Called when the BFF reports the session is gone (refresh already failed server-side). */
  onSessionExpired?: () => void;
  fetch?: typeof fetch;
  basePath?: string;
}

/**
 * Browser client: talks only to the same-origin BFF (/api/bff/*). Tokens never reach JavaScript;
 * the BFF attaches them from httpOnly cookies and refreshes them. Mutations carry the CSRF token.
 */
export function createBrowserClient(options: BrowserClientOptions): ApiClient {
  return createApiClient({
    baseUrl: options.basePath ?? BFF_BASE_PATH,
    fetch: options.fetch,
    headers: (method) => {
      const headers: Record<string, string> = { 'Accept-Language': options.locale };
      if (method !== 'GET') {
        const csrf = readCookie(cookieNames(options.appId).csrf);
        if (csrf) headers[CSRF_HEADER] = csrf;
      }
      return headers;
    },
    onUnauthorized: async () => {
      options.onSessionExpired?.();
      return false;
    },
  });
}

export function createBrowserEndpoints(options: BrowserClientOptions) {
  return createEndpoints(createBrowserClient(options));
}
