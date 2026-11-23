/**
 * RFC 7807 problem+json handling. The backend adds stable `code`, `params`, `errors` and `traceId`.
 * The UI must translate `code` (and each field error code) — never display server English `title`/`detail`.
 */

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  params?: Record<string, unknown>;
  errors?: Record<string, string[]>;
  traceId?: string;
}

/** Well-known error codes from the contract plus client-side codes. */
export const ErrorCodes = {
  invalidCredentials: 'auth.invalid_credentials',
  unauthenticated: 'auth.unauthenticated',
  validationFailed: 'validation.failed',
  tenantForbidden: 'tenant.forbidden',
  notFound: 'resource.not_found',
  concurrencyConflict: 'concurrency.conflict',
  csrfFailed: 'security.csrf_failed',
  network: 'client.network_error',
  unexpected: 'client.unexpected_error',
  server: 'server.error',
} as const;

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly params: Record<string, unknown>;
  readonly fieldErrors: Record<string, string[]>;
  readonly traceId: string | undefined;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.code ?? `http_${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.code = problem.code ?? fallbackCode(status);
    this.params = problem.params ?? {};
    this.fieldErrors = normalizeFieldErrors(problem.errors);
    this.traceId = problem.traceId;
  }

  get isConflict(): boolean {
    return this.status === 409 || this.code === ErrorCodes.concurrencyConflict;
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  get isForbidden(): boolean {
    return this.status === 403;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  get isValidation(): boolean {
    return this.status === 400 || this.status === 422 || this.code === ErrorCodes.validationFailed;
  }
}

export function fallbackCode(status: number): string {
  if (status === 0) return ErrorCodes.network;
  if (status === 401) return ErrorCodes.unauthenticated;
  if (status === 403) return ErrorCodes.tenantForbidden;
  if (status === 404) return ErrorCodes.notFound;
  if (status === 409) return ErrorCodes.concurrencyConflict;
  if (status === 400 || status === 422) return ErrorCodes.validationFailed;
  if (status >= 500) return ErrorCodes.server;
  return ErrorCodes.unexpected;
}

/** Field keys are camelCase; ASP.NET may send PascalCase or "$.field" paths — normalize them. */
function normalizeFieldErrors(errors: ProblemDetails['errors']): Record<string, string[]> {
  if (!errors || typeof errors !== 'object') return {};
  const out: Record<string, string[]> = {};
  for (const [rawKey, value] of Object.entries(errors)) {
    const key = rawKey.replace(/^\$\./, '');
    const normalized = key.length > 0 ? key[0]!.toLowerCase() + key.slice(1) : key;
    out[normalized] = Array.isArray(value) ? value.map(String) : [String(value)];
  }
  return out;
}

export function isProblemContentType(contentType: string | null): boolean {
  return !!contentType && /application\/(problem\+)?json/i.test(contentType);
}

/** Build an ApiError from any fetch Response that is not ok. Never throws. */
export async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {};
  if (isProblemContentType(response.headers.get('content-type'))) {
    try {
      problem = (await response.json()) as ProblemDetails;
    } catch {
      problem = {};
    }
  }
  return new ApiError(response.status, problem);
}

export function isApiError(value: unknown): value is ApiError {
  return value instanceof ApiError;
}
