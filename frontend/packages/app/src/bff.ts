import { ApiError, CSRF_HEADER, cookieNames, ErrorCodes, readCookie, toApiError } from '@eemp/api-client';

/** POST to one of this app's own /api/auth/* routes with the double-submit CSRF header. */
export async function postAuth<T>(appId: string, path: 'login' | 'logout', body?: unknown, locale?: string): Promise<T> {
  const csrf = readCookie(cookieNames(appId).csrf) ?? '';
  let response: Response;
  try {
    response = await fetch(`/api/auth/${path}`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'application/json',
        [CSRF_HEADER]: csrf,
        ...(locale ? { 'Accept-Language': locale } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, { code: ErrorCodes.network });
  }
  if (!response.ok) throw await toApiError(response);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}
