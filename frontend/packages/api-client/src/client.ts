import { ApiError, ErrorCodes, toApiError } from './problem';

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH';
export type QueryValue = string | number | boolean | null | undefined;

export interface RequestOptions {
  query?: Record<string, QueryValue>;
  body?: unknown;
  signal?: AbortSignal;
  headers?: Record<string, string>;
}

export interface ApiClientOptions {
  /** e.g. "/api/bff" in the browser (BFF) or "http://api:8080/api/v1" on the server. */
  baseUrl: string;
  fetch?: typeof fetch;
  /** Extra headers per request (Accept-Language, Authorization, X-CSRF-Token, ...). */
  headers?: (method: HttpMethod) => Record<string, string> | Promise<Record<string, string>>;
  /**
   * Called once when a request returns 401. Resolve true after a successful token refresh
   * to retry the request one time; false to surface the 401.
   */
  onUnauthorized?: () => Promise<boolean>;
  /** Generates the X-Correlation-Id for each request. */
  correlationId?: () => string;
}

/** A binary response (PDF, ZIP) with the server-suggested file name from Content-Disposition. */
export interface DownloadedFile {
  blob: Blob;
  fileName: string | null;
  contentType: string;
}

/** File name from a Content-Disposition header (RFC 6266: filename* wins over filename). */
export function fileNameFromDisposition(header: string | null): string | null {
  if (!header) return null;
  const star = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header);
  if (star?.[1]) {
    try {
      return decodeURIComponent(star[1].trim().replace(/^"|"$/g, ''));
    } catch {
      /* fall through to the plain parameter */
    }
  }
  const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/.exec(header);
  const value = plain?.[2] ?? plain?.[1];
  return value ? value.trim() : null;
}

export interface ApiClient {
  request<T>(method: HttpMethod, path: string, options?: RequestOptions): Promise<T>;
  get<T>(path: string, options?: Omit<RequestOptions, 'body'>): Promise<T>;
  post<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T>;
  put<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T>;
  delete<T = void>(path: string, options?: RequestOptions): Promise<T>;
  /** GET a binary resource; problem+json errors are thrown as ApiError like any other call. */
  download(path: string, options?: Omit<RequestOptions, 'body'>): Promise<DownloadedFile>;
}

export function buildUrl(baseUrl: string, path: string, query?: Record<string, QueryValue>): string {
  const base = baseUrl.replace(/\/+$/, '');
  const suffix = path.startsWith('/') ? path : `/${path}`;
  const params = new URLSearchParams();
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === undefined || value === null || value === '') continue;
      params.set(key, String(value));
    }
  }
  const qs = params.toString();
  return `${base}${suffix}${qs ? `?${qs}` : ''}`;
}

function defaultCorrelationId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

export function createApiClient(options: ApiClientOptions): ApiClient {
  const doFetch = options.fetch ?? ((input, init) => globalThis.fetch(input, init));
  const correlationId = options.correlationId ?? defaultCorrelationId;

  async function send(method: HttpMethod, path: string, opts: RequestOptions, accept = 'application/json, application/problem+json'): Promise<Response> {
    const extra = (await options.headers?.(method)) ?? {};
    const headers: Record<string, string> = {
      Accept: accept,
      'X-Correlation-Id': correlationId(),
      ...extra,
      ...opts.headers,
    };
    let body: BodyInit | undefined;
    if (opts.body !== undefined) {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(opts.body);
    }
    try {
      return await doFetch(buildUrl(options.baseUrl, path, opts.query), {
        method,
        headers,
        body,
        signal: opts.signal,
        credentials: 'same-origin',
        cache: 'no-store',
      });
    } catch (error) {
      if ((error as { name?: string })?.name === 'AbortError') throw error;
      throw new ApiError(0, { code: ErrorCodes.network });
    }
  }

  async function request<T>(method: HttpMethod, path: string, opts: RequestOptions = {}): Promise<T> {
    let response = await send(method, path, opts);
    if (response.status === 401 && options.onUnauthorized) {
      const refreshed = await options.onUnauthorized();
      if (refreshed) response = await send(method, path, opts);
    }
    if (!response.ok) throw await toApiError(response);
    if (response.status === 204) return undefined as T;
    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  async function download(path: string, opts: Omit<RequestOptions, 'body'> = {}): Promise<DownloadedFile> {
    const accept = 'application/pdf, application/zip, application/octet-stream, application/problem+json';
    let response = await send('GET', path, opts, accept);
    if (response.status === 401 && options.onUnauthorized) {
      const refreshed = await options.onUnauthorized();
      if (refreshed) response = await send('GET', path, opts, accept);
    }
    if (!response.ok) throw await toApiError(response);
    return {
      blob: await response.blob(),
      fileName: fileNameFromDisposition(response.headers.get('content-disposition')),
      contentType: response.headers.get('content-type') ?? 'application/octet-stream',
    };
  }

  return {
    request,
    download,
    get: (path, opts) => request('GET', path, opts),
    post: (path, body, opts) => request('POST', path, { ...opts, body }),
    put: (path, body, opts) => request('PUT', path, { ...opts, body }),
    delete: (path, opts) => request('DELETE', path, opts),
  };
}
