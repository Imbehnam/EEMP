/**
 * Maps backend problem+json `code` values (and field error codes) to translated messages under
 * the `errors` namespace. Server-provided English text is never shown.
 */
export interface ErrorLike {
  code: string;
  params?: Record<string, unknown>;
  traceId?: string;
}

export interface ErrorTranslator {
  (key: string, values?: Record<string, string | number | Date>): string;
  has(key: string): boolean;
}

function toValues(params: Record<string, unknown> | undefined): Record<string, string | number | Date> {
  const values: Record<string, string | number | Date> = {};
  for (const [key, value] of Object.entries(params ?? {})) {
    if (typeof value === 'string' || typeof value === 'number' || value instanceof Date) values[key] = value;
    else if (value !== null && value !== undefined) values[key] = String(value);
  }
  return values;
}

/** `t` must be scoped to the `errors` namespace. */
export function translateErrorCode(t: ErrorTranslator, code: string, params?: Record<string, unknown>): string {
  if (code && t.has(code)) {
    try {
      return t(code, toValues(params));
    } catch {
      // fall through to generic message (e.g. ICU argument missing)
    }
  }
  return t('unknown', { code: code || '?' });
}

export function translateError(t: ErrorTranslator, error: ErrorLike): string {
  return translateErrorCode(t, error.code, error.params);
}

export function translateFieldErrors(t: ErrorTranslator, fieldErrors: Record<string, string[]>): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [field, codes] of Object.entries(fieldErrors)) {
    const first = codes[0];
    if (first) result[field] = translateErrorCode(t, first);
  }
  return result;
}
