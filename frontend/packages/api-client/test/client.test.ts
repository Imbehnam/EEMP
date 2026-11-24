import { describe, expect, it, vi } from 'vitest';

import { ApiError, buildUrl, createApiClient, ErrorCodes, toApiError, toPaged } from '../src';

const problem = (status: number, body: object) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });

describe('buildUrl', () => {
  it('joins paths and drops empty query values', () => {
    expect(buildUrl('/api/bff/', 'branches', { page: 2, search: '', pageSize: undefined })).toBe('/api/bff/branches?page=2');
  });
});

describe('problem+json', () => {
  it('maps code, params, traceId and normalizes PascalCase / $. field keys', async () => {
    const error = await toApiError(
      problem(400, { code: 'validation.failed', params: { max: 3 }, errors: { FirstName: ['validation.required'], '$.email': ['validation.invalid_email'] }, traceId: 't-1' }),
    );
    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe('validation.failed');
    expect(error.isValidation).toBe(true);
    expect(error.fieldErrors).toEqual({ firstName: ['validation.required'], email: ['validation.invalid_email'] });
    expect(error.traceId).toBe('t-1');
    expect(error.params).toEqual({ max: 3 });
  });

  it('falls back to a status-derived code when the body is not problem+json', async () => {
    const error = await toApiError(new Response('oops', { status: 409, headers: { 'Content-Type': 'text/plain' } }));
    expect(error.code).toBe(ErrorCodes.concurrencyConflict);
    expect(error.isConflict).toBe(true);
  });
});

describe('createApiClient', () => {
  it('sends JSON with correlation id and custom headers', async () => {
    const fetch = vi.fn(async () => new Response(JSON.stringify({ ok: 1 }), { status: 200 }));
    const client = createApiClient({ baseUrl: '/api/bff', fetch, headers: () => ({ 'Accept-Language': 'fa' }), correlationId: () => 'c-1' });
    await expect(client.post('/branches', { a: 1 })).resolves.toEqual({ ok: 1 });
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/bff/branches');
    expect(init.body).toBe('{"a":1}');
    expect(init.headers).toMatchObject({ 'Accept-Language': 'fa', 'X-Correlation-Id': 'c-1', 'Content-Type': 'application/json' });
  });

  it('retries once after a successful refresh on 401', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(problem(401, { code: 'auth.unauthenticated' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    const onUnauthorized = vi.fn(async () => true);
    const client = createApiClient({ baseUrl: '/x', fetch, onUnauthorized });
    await expect(client.delete('/branches/1')).resolves.toBeUndefined();
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it('surfaces network failures as client.network_error', async () => {
    const client = createApiClient({ baseUrl: '/x', fetch: vi.fn().mockRejectedValue(new TypeError('failed')) });
    await expect(client.get('/a')).rejects.toMatchObject({ code: ErrorCodes.network, status: 0 });
  });
});

describe('toPaged', () => {
  it('normalizes plain arrays and passes paged results through', () => {
    expect(toPaged([1, 2])).toEqual({ items: [1, 2], page: 1, pageSize: 2, totalCount: 2 });
    const paged = { items: [1], page: 2, pageSize: 1, totalCount: 5 };
    expect(toPaged(paged)).toBe(paged);
  });
});
