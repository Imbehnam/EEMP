import { beforeEach, describe, expect, it } from 'vitest';

import { createMockBackend, DEFAULT_MOCK_PASSWORD, MOCK_API_BASE_URL, MOCK_IDS } from '../src/mock';
import type { Paged, Permission, PortalProfile, PortalStudent, Role, Student, TenantUser, TokenResponse } from '../src';

let backend: ReturnType<typeof createMockBackend>;

const call = async (method: string, path: string, opts: { token?: string; body?: unknown } = {}) => {
  const response = await backend.fetch(`${MOCK_API_BASE_URL}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(opts.token ? { Authorization: `Bearer ${opts.token}` } : {}) },
    body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
  });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : undefined, contentType: response.headers.get('content-type') };
};

const login = async (tenantSlug: string | null, userName: string) => {
  const r = await call('POST', '/auth/login', { body: { tenantSlug, userName, password: DEFAULT_MOCK_PASSWORD } });
  expect(r.status).toBe(200);
  return (r.body as TokenResponse).accessToken;
};

beforeEach(() => {
  backend = createMockBackend();
});

describe('mock backend: auth', () => {
  it('returns a uniform problem for unknown tenant, unknown user and wrong password', async () => {
    for (const body of [
      { tenantSlug: 'nope', userName: 'demo.admin', password: DEFAULT_MOCK_PASSWORD },
      { tenantSlug: 'demo', userName: 'ghost', password: DEFAULT_MOCK_PASSWORD },
      { tenantSlug: 'demo', userName: 'demo.admin', password: 'wrong' },
    ]) {
      const r = await call('POST', '/auth/login', { body });
      expect(r.status).toBe(401);
      expect(r.contentType).toContain('application/problem+json');
      expect(r.body.code).toBe('auth.invalid_credentials');
    }
  });

  it('rotates refresh tokens (single use)', async () => {
    const first = (await call('POST', '/auth/login', { body: { tenantSlug: 'demo', userName: 'demo.admin', password: DEFAULT_MOCK_PASSWORD } })).body as TokenResponse;
    const refreshed = await call('POST', '/auth/refresh', { body: { refreshToken: first.refreshToken } });
    expect(refreshed.status).toBe(200);
    const reused = await call('POST', '/auth/refresh', { body: { refreshToken: first.refreshToken } });
    expect(reused.status).toBe(401);
  });

  it('deactivated users are rejected on their next request', async () => {
    const admin = await login('demo', 'demo.admin');
    const teacher = await login('demo', 'teacher.sara');
    expect((await call('GET', '/auth/me', { token: teacher })).status).toBe(200);
    expect((await call('POST', `/users/${MOCK_IDS.userTeacher}/deactivate`, { token: admin })).status).toBe(200);
    expect((await call('GET', '/auth/me', { token: teacher })).status).toBe(401);
  });

  it('suspending a tenant blocks its users', async () => {
    const platform = await login(null, 'platform.admin');
    const admin = await login('demo', 'demo.admin');
    expect((await call('POST', `/platform/tenants/${MOCK_IDS.tenantDemo}/suspend`, { token: platform })).body.status).toBe('suspended');
    const r = await call('GET', '/branches', { token: admin });
    expect(r.status).toBe(403);
    expect(r.body.code).toBe('tenant.suspended');
  });
});

describe('mock backend: contract clarifications (2026-11-24)', () => {
  it('permissions carry isSensitive and branchScoped', async () => {
    const token = await login('demo', 'demo.admin');
    const permissions = (await call('GET', '/permissions', { token })).body as Permission[];
    expect(permissions.find((p) => p.code === 'students.read')).toEqual({ code: 'students.read', module: 'people', isSensitive: true, branchScoped: true });
    expect(permissions.find((p) => p.code === 'organization.read')).toMatchObject({ isSensitive: false, branchScoped: false });
    expect(permissions.every((p) => /^[a-z]+\.(read|manage|approve|publish)$/.test(p.code))).toBe(true);
  });

  it('roles and portal students are plain arrays; users are paged with roleCode assignments', async () => {
    const admin = await login('demo', 'demo.admin');
    const roles = (await call('GET', '/roles', { token: admin })).body as Role[];
    expect(Array.isArray(roles)).toBe(true);
    const users = (await call('GET', '/users?pageSize=100', { token: admin })).body as Paged<TenantUser>;
    expect(users.totalCount).toBeGreaterThan(0);
    const demoAdmin = users.items.find((u) => u.userName === 'demo.admin')!;
    expect(Object.keys(demoAdmin).sort()).toEqual(
      ['assignments', 'displayName', 'email', 'id', 'isActive', 'locale', 'phone', 'rowVersion', 'timeZone', 'userName'].sort(),
    );
    expect(demoAdmin.assignments[0]).toEqual({ roleId: MOCK_IDS.roleTenantAdmin, roleCode: 'TenantAdmin', branchId: null });

    const guardian = await login('demo', 'guardian.ali');
    const students = (await call('GET', '/portal/me/students', { token: guardian })).body as PortalStudent[];
    expect(Array.isArray(students)).toBe(true);
    expect(students.map((s) => s.studentNumber).sort()).toEqual(['ST-1001', 'ST-1002']);
    // Same summary shape as the real API (settled 2026-11-24).
    expect(Object.keys(students[0]!).sort()).toEqual(
      ['branchId', 'firstName', 'id', 'isPrimary', 'lastName', 'relationship', 'studentNumber'].sort(),
    );
  });

  it('portal profile is { kind, profile }', async () => {
    const student = await login('demo', 'student.reza');
    const profile = (await call('GET', '/portal/me/profile', { token: student })).body as PortalProfile;
    expect(profile.kind).toBe('student');
    expect(profile.profile.id).toBe(MOCK_IDS.student1);
  });

  it('enums are camelCase strings and studentNumber is generated when omitted', async () => {
    const admin = await login('demo', 'demo.admin');
    const created = await call('POST', '/students', {
      token: admin,
      body: { branchId: MOCK_IDS.branchNorth, firstName: 'Nima', lastName: 'Rahimi', gender: 'male', status: 'active' },
    });
    expect(created.status).toBe(201);
    expect(created.body.studentNumber).toMatch(/^ST-\d+$/);
    expect(created.body.gender).toBe('male');
    const bad = await call('POST', '/students', { token: admin, body: { branchId: MOCK_IDS.branchNorth, firstName: 'A', lastName: 'B', gender: 'Male' } });
    expect(bad.status).toBe(400);
    expect(bad.body.errors.gender).toEqual(['validation.invalid_format']);
  });

  it('DELETE needs no rowVersion', async () => {
    const admin = await login('demo', 'demo.admin');
    const created = await call('POST', '/branches', { token: admin, body: { code: 'E01', name: 'East' } });
    expect((await call('DELETE', `/branches/${created.body.id}`, { token: admin })).status).toBe(204);
  });
});

describe('mock backend: isolation and scope', () => {
  it('does not leak records across tenants', async () => {
    const alborz = await login('alborz', 'alborz.admin');
    const r = await call('GET', `/branches/${MOCK_IDS.branchNorth}`, { token: alborz });
    expect(r.status).toBe(404);
    const list = (await call('GET', '/branches', { token: alborz })).body as Paged<{ id: string }>;
    expect(list.items.map((b) => b.id)).toEqual([MOCK_IDS.branchAlborz]);
  });

  it('limits branch-scoped people to the assigned branch (out-of-scope looks missing)', async () => {
    const south = await login('demo', 'manager.south');
    const list = (await call('GET', '/students', { token: south })).body as Paged<Student>;
    expect(list.items.map((s) => s.id)).toEqual([MOCK_IDS.student3]);
    expect((await call('GET', `/students/${MOCK_IDS.student1}`, { token: south })).status).toBe(404);
  });

  it('tenant admin role has no implicit access to people records', async () => {
    const token = await login('alborz', 'alborz.admin');
    expect((await call('GET', '/students', { token })).status).toBe(403);
  });

  it('platform admins cannot read tenant data and tenant users cannot reach platform routes', async () => {
    const platform = await login(null, 'platform.admin');
    expect((await call('GET', '/branches', { token: platform })).status).toBe(403);
    const admin = await login('demo', 'demo.admin');
    expect((await call('GET', '/platform/tenants', { token: admin })).status).toBe(403);
  });

  it('enforces optimistic concurrency with rowVersion', async () => {
    const admin = await login('demo', 'demo.admin');
    const branch = (await call('GET', `/branches/${MOCK_IDS.branchNorth}`, { token: admin })).body;
    backend.touch('branches', 'demo', MOCK_IDS.branchNorth);
    const r = await call('PUT', `/branches/${branch.id}`, { token: admin, body: { ...branch, name: 'Renamed' } });
    expect(r.status).toBe(409);
    expect(r.body.code).toBe('concurrency.conflict');
  });
});
