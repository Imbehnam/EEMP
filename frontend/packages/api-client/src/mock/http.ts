import { MAX_PAGE_SIZE } from '../types';

export class HttpError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly extra: { errors?: Record<string, string[]>; params?: Record<string, unknown> } = {},
  ) {
    super(code);
  }
}

export const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(status === 204 ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json; charset=utf-8', ...headers },
  });

export function problem(error: HttpError, traceId: string) {
  return new Response(
    JSON.stringify({
      type: `https://eemp.dev/problems/${error.code}`,
      title: 'Mock error',
      status: error.status,
      code: error.code,
      params: error.extra.params ?? {},
      errors: error.extra.errors,
      traceId,
    }),
    { status: error.status, headers: { 'Content-Type': 'application/problem+json; charset=utf-8' } },
  );
}

export const randomToken = () =>
  Array.from(globalThis.crypto.getRandomValues(new Uint8Array(24)), (b) => b.toString(16).padStart(2, '0')).join('');

export const uuid = () => globalThis.crypto.randomUUID();

export function required(body: Record<string, unknown>, fields: string[]) {
  const errors: Record<string, string[]> = {};
  for (const field of fields) {
    const value = body[field];
    if (value === undefined || value === null || (typeof value === 'string' && value.trim() === '')) {
      errors[field] = ['validation.required'];
    }
  }
  return errors;
}

export function assertValid(errors: Record<string, string[]>) {
  if (Object.keys(errors).length > 0) throw new HttpError(400, 'validation.failed', { errors });
}

export function emailCheck(errors: Record<string, string[]>, body: Record<string, unknown>, field = 'email') {
  const value = body[field];
  if (typeof value === 'string' && value !== '' && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) {
    errors[field] = ['validation.invalid_email'];
  }
}

export function paginate<T>(items: T[], url: URL, searchFields: (item: T) => (string | null | undefined)[]) {
  const page = Math.max(1, Number(url.searchParams.get('page') ?? '1') || 1);
  const pageSize = Math.min(MAX_PAGE_SIZE, Math.max(1, Number(url.searchParams.get('pageSize') ?? '20') || 20));
  const search = (url.searchParams.get('search') ?? '').trim().toLocaleLowerCase();
  const filtered = search
    ? items.filter((item) => searchFields(item).some((v) => (v ?? '').toLocaleLowerCase().includes(search)))
    : items;
  return {
    items: filtered.slice((page - 1) * pageSize, page * pageSize),
    page,
    pageSize,
    totalCount: filtered.length,
  };
}

