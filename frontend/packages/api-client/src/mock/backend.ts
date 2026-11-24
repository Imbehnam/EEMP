/**
 * In-process mock of the EEMP Milestone 1 API (development and tests only).
 * It implements the shared contract's routes, paging, problem+json errors, rowVersion
 * concurrency, token rotation and a simplified permission + branch-scope model so the
 * admin and client apps run without the .NET backend (EEMP_API_MOCK=true).
 * It is NOT a security boundary and must never be enabled in a real deployment.
 */
import type {
  Branch,
  CurrentUser,
  Guardian,
  PersonBase,
  PersonKind,
  Role,
  Staff,
  Student,
  Teacher,
  Tenant,
  TokenResponse,
} from '../types';
import { GENDERS, MAX_PAGE_SIZE, PERSON_KINDS, PERSON_STATUSES, type Gender, type PersonStatus } from '../types';
import { createSeed, PERMISSIONS, type MockState, type MockTenantData, type MockUser } from './seed';

export const MOCK_API_BASE_URL = 'http://mock.eemp.invalid/api/v1';
export const DEFAULT_MOCK_PASSWORD = 'eemp-mock';
const ACCESS_TTL_MS = 15 * 60 * 1000;

export interface MockBackendOptions {
  /** Password accepted for every seeded user. Mock-only, never a real credential. */
  password?: string;
  now?: () => number;
}

export interface MockBackend {
  fetch: typeof fetch;
  reset(): void;
  state(): MockState;
  /** Test helper: bump a resource's rowVersion as if another user edited it. */
  touch(kind: 'branches' | 'organization', tenantSlug: string, id?: string): void;
}

interface Session {
  userId: string;
  expiresAt: number;
}

class HttpError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly extra: { errors?: Record<string, string[]>; params?: Record<string, unknown> } = {},
  ) {
    super(code);
  }
}

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(status === 204 ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json; charset=utf-8', ...headers },
  });

function problem(error: HttpError, traceId: string) {
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

const randomToken = () =>
  Array.from(globalThis.crypto.getRandomValues(new Uint8Array(24)), (b) => b.toString(16).padStart(2, '0')).join('');

const uuid = () => globalThis.crypto.randomUUID();

function required(body: Record<string, unknown>, fields: string[]) {
  const errors: Record<string, string[]> = {};
  for (const field of fields) {
    const value = body[field];
    if (value === undefined || value === null || (typeof value === 'string' && value.trim() === '')) {
      errors[field] = ['validation.required'];
    }
  }
  return errors;
}

function assertValid(errors: Record<string, string[]>) {
  if (Object.keys(errors).length > 0) throw new HttpError(400, 'validation.failed', { errors });
}

function emailCheck(errors: Record<string, string[]>, body: Record<string, unknown>, field = 'email') {
  const value = body[field];
  if (typeof value === 'string' && value !== '' && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) {
    errors[field] = ['validation.invalid_email'];
  }
}

function paginate<T>(items: T[], url: URL, searchFields: (item: T) => (string | null | undefined)[]) {
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

export function createMockBackend(options: MockBackendOptions = {}): MockBackend {
  const password = options.password || DEFAULT_MOCK_PASSWORD;
  const now = options.now ?? Date.now;
  let state = createSeed();
  let access = new Map<string, Session>();
  let refresh = new Map<string, string>();

  const nextVersion = () => btoa(`v${++state.version}`);

  function tenantData(user: MockUser): MockTenantData {
    if (!user.tenantId) throw new HttpError(403, 'tenant.forbidden');
    const data = state.data[user.tenantId];
    if (!data) throw new HttpError(403, 'tenant.forbidden');
    return data;
  }

  function permissionsFor(user: MockUser, branchId?: string | null): Set<string> {
    if (!user.tenantId) return new Set();
    const roles = state.data[user.tenantId]?.roles ?? [];
    const result = new Set<string>();
    for (const a of user.assignments) {
      if (branchId !== undefined && a.branchId !== null && a.branchId !== branchId) continue;
      roles.find((r) => r.id === a.roleId)?.permissions.forEach((p) => result.add(p));
    }
    return result;
  }

  function demand(user: MockUser, permission: string, branchId?: string | null) {
    if (user.isPlatformAdmin) throw new HttpError(403, 'tenant.forbidden');
    if (!permissionsFor(user, branchId).has(permission)) throw new HttpError(403, 'auth.forbidden');
  }

  /** Branches where the user holds a permission (null => all branches of the tenant). */
  function branchesWith(user: MockUser, permission: string): Set<string> | null {
    const roles = state.data[user.tenantId ?? '']?.roles ?? [];
    const result = new Set<string>();
    for (const a of user.assignments) {
      const role = roles.find((r) => r.id === a.roleId);
      if (!role?.permissions.includes(permission)) continue;
      if (a.branchId === null) return null;
      result.add(a.branchId);
    }
    return result;
  }

  function me(user: MockUser): CurrentUser {
    const tenant = state.tenants.find((t) => t.id === user.tenantId);
    const roles = user.tenantId ? (state.data[user.tenantId]?.roles ?? []) : [];
    return {
      id: user.id,
      userName: user.userName,
      displayName: user.displayName,
      tenantId: user.tenantId,
      tenantName: tenant?.name ?? null,
      locale: user.locale ?? tenant?.defaultLocale ?? 'fa',
      timeZone: user.timeZone ?? tenant?.timeZone ?? 'Asia/Tehran',
      isPlatformAdmin: user.isPlatformAdmin,
      permissions: [...permissionsFor(user)].sort(),
      scopes: user.assignments.map((a) => ({
        roleCode: roles.find((r) => r.id === a.roleId)?.code ?? 'unknown',
        branchId: a.branchId,
      })),
    };
  }

  function issueTokens(user: MockUser): TokenResponse {
    const accessToken = `mock-at-${randomToken()}`;
    const refreshToken = `mock-rt-${randomToken()}`;
    const expiresAt = now() + ACCESS_TTL_MS;
    access.set(accessToken, { userId: user.id, expiresAt });
    refresh.set(refreshToken, user.id);
    return { accessToken, accessTokenExpiresAt: new Date(expiresAt).toISOString(), refreshToken, user: me(user) };
  }

  function authenticate(request: Request): MockUser {
    const header = request.headers.get('authorization') ?? '';
    const token = header.startsWith('Bearer ') ? header.slice(7) : '';
    const session = access.get(token);
    if (!session || session.expiresAt <= now()) throw new HttpError(401, 'auth.unauthenticated');
    const user = state.users.find((u) => u.id === session.userId);
    // Deactivation takes effect on the next request (security baseline #5).
    if (!user || !user.isActive) throw new HttpError(401, 'auth.unauthenticated');
    if (user.tenantId) {
      const tenant = state.tenants.find((t) => t.id === user.tenantId);
      if (!tenant || tenant.status !== 'active') throw new HttpError(403, 'tenant.suspended');
    }
    return user;
  }

  function checkVersion(current: string, body: Record<string, unknown>) {
    if (typeof body.rowVersion !== 'string' || body.rowVersion === '') {
      throw new HttpError(400, 'validation.failed', { errors: { rowVersion: ['validation.required'] } });
    }
    if (body.rowVersion !== current) throw new HttpError(409, 'concurrency.conflict');
  }

  async function readBody(request: Request): Promise<Record<string, unknown>> {
    const text = await request.text();
    if (!text) return {};
    try {
      return JSON.parse(text) as Record<string, unknown>;
    } catch {
      throw new HttpError(400, 'validation.failed', { errors: { body: ['validation.invalid_format'] } });
    }
  }

  /* ---------------- route handlers ---------------- */

  async function handle(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const path = url.pathname.replace(/^.*\/api\/v1/, '').replace(/\/+$/, '') || '/';
    const segments = path.split('/').filter(Boolean);
    const method = request.method.toUpperCase();
    const body = ['POST', 'PUT', 'PATCH'].includes(method) ? await readBody(request) : {};

    // ---- auth (anonymous) ----
    if (path === '/auth/login' && method === 'POST') {
      const tenantSlug = (body.tenantSlug as string | null | undefined) || null;
      const errors = required(body, ['userName', 'password']);
      assertValid(errors);
      const tenant = tenantSlug ? state.tenants.find((t) => t.slug === tenantSlug.toLowerCase()) : null;
      const user = state.users.find(
        (u) =>
          u.userName.toLowerCase() === String(body.userName).toLowerCase() &&
          (tenantSlug ? u.tenantId === tenant?.id : u.tenantId === null),
      );
      // Uniform response: never reveal whether tenant or user exists.
      if (!user || !user.isActive || body.password !== password) throw new HttpError(401, 'auth.invalid_credentials');
      if (tenant && tenant.status !== 'active') throw new HttpError(403, 'tenant.suspended');
      return json(200, issueTokens(user));
    }
    if (path === '/auth/refresh' && method === 'POST') {
      const token = String(body.refreshToken ?? '');
      const userId = refresh.get(token);
      refresh.delete(token); // rotation: a refresh token is single-use
      const user = state.users.find((u) => u.id === userId);
      if (!user || !user.isActive) throw new HttpError(401, 'auth.invalid_refresh_token');
      return json(200, issueTokens(user));
    }
    if (path === '/auth/logout' && method === 'POST') {
      refresh.delete(String(body.refreshToken ?? ''));
      return json(204, null);
    }

    const user = authenticate(request);

    if (path === '/auth/me' && method === 'GET') return json(200, me(user));
    if (path === '/auth/me/preferences' && method === 'PUT') {
      const errors = required(body, ['locale', 'timeZone']);
      if (body.locale && !['fa', 'en', 'tr', 'ar'].includes(String(body.locale))) errors.locale = ['validation.unsupported_locale'];
      if (body.timeZone && !isValidTimeZone(String(body.timeZone))) errors.timeZone = ['validation.invalid_time_zone'];
      assertValid(errors);
      user.locale = String(body.locale);
      user.timeZone = String(body.timeZone);
      return json(204, null);
    }

    // ---- platform ----
    if (segments[0] === 'platform') {
      if (!user.isPlatformAdmin) throw new HttpError(403, 'auth.forbidden');
      return platformRoutes(segments.slice(1), method, url, body);
    }
    if (user.isPlatformAdmin) throw new HttpError(403, 'tenant.forbidden'); // platform ops ≠ tenant data

    const data = tenantData(user);
    switch (segments[0]) {
      case 'organization':
        return organizationRoutes(user, data, method, body);
      case 'branches':
        return branchRoutes(user, data, segments, method, url, body);
      case 'permissions':
        demand(user, 'roles.read');
        return json(200, PERMISSIONS);
      case 'roles':
        return roleRoutes(user, data, segments, method, url, body);
      case 'users':
        return userRoutes(user, data, segments, method, url, body);
      case 'portal':
        return portalRoutes(user, data, segments);
      default:
        if (PERSON_KINDS.includes(segments[0] as PersonKind)) {
          return peopleRoutes(user, data, segments[0] as PersonKind, segments, method, url, body);
        }
    }
    throw new HttpError(404, 'resource.not_found');
  }

  function platformRoutes(seg: string[], method: string, url: URL, body: Record<string, unknown>): Response {
    if (seg[0] !== 'tenants') throw new HttpError(404, 'resource.not_found');
    if (seg.length === 1 && method === 'GET') {
      return json(200, paginate(state.tenants, url, (t) => [t.name, t.slug]));
    }
    if (seg.length === 1 && method === 'POST') {
      const errors = required(body, ['slug', 'name', 'defaultLocale', 'timeZone', 'currency']);
      const admin = (body.adminUser ?? {}) as Record<string, unknown>;
      const adminErrors = required(admin, ['userName', 'displayName', 'password']);
      for (const [k, v] of Object.entries(adminErrors)) errors[`adminUser.${k}`] = v;
      emailCheck(errors, admin, 'email');
      if (errors.email) {
        errors['adminUser.email'] = errors.email;
        delete errors.email;
      }
      if (typeof body.slug === 'string' && !/^[a-z0-9](?:[a-z0-9-]{1,38}[a-z0-9])$/.test(body.slug)) errors.slug = ['validation.invalid_slug'];
      if (typeof body.slug === 'string' && state.tenants.some((t) => t.slug === body.slug)) errors.slug = ['validation.duplicate'];
      assertValid(errors);
      const tenant: Tenant = {
        id: uuid(),
        slug: String(body.slug),
        name: String(body.name),
        defaultLocale: String(body.defaultLocale),
        supportedLocales: (body.supportedLocales as string[]) ?? [String(body.defaultLocale)],
        timeZone: String(body.timeZone),
        currency: String(body.currency),
        status: 'active',
        createdAt: new Date(now()).toISOString(),
        rowVersion: nextVersion(),
      };
      state.tenants.push(tenant);
      const adminRole: Role = {
        id: uuid(), code: 'TenantAdmin', name: 'Tenant administrator', isSystem: true,
        permissions: PERMISSIONS.map((p) => p.code).filter((c) => !/^(students|guardians|teachers|staff)\./.test(c)),
        rowVersion: nextVersion(),
      };
      state.data[tenant.id] = {
        organization: { name: tenant.name, legalName: null, phone: null, email: null, address: null, defaultLocale: tenant.defaultLocale, timeZone: tenant.timeZone, rowVersion: nextVersion() },
        branches: [], roles: [adminRole], students: [], guardians: [], teachers: [], staff: [],
      };
      state.users.push({
        id: uuid(), tenantId: tenant.id, userName: String(admin.userName), displayName: String(admin.displayName),
        email: (admin.email as string) || null, phone: (admin.phone as string) || null, locale: tenant.defaultLocale,
        timeZone: tenant.timeZone, isActive: true, isPlatformAdmin: false,
        assignments: [{ roleId: adminRole.id, branchId: null }], rowVersion: nextVersion(),
      });
      return json(201, tenant, { Location: `/api/v1/platform/tenants/${tenant.id}` });
    }
    const tenant = state.tenants.find((t) => t.id === seg[1]);
    if (!tenant) throw new HttpError(404, 'resource.not_found');
    if (seg.length === 2 && method === 'GET') return json(200, tenant);
    if (seg.length === 2 && method === 'PUT') {
      checkVersion(tenant.rowVersion, body);
      const errors = required(body, ['name', 'defaultLocale', 'timeZone', 'currency']);
      assertValid(errors);
      Object.assign(tenant, {
        name: body.name, defaultLocale: body.defaultLocale, supportedLocales: body.supportedLocales ?? tenant.supportedLocales,
        timeZone: body.timeZone, currency: body.currency, rowVersion: nextVersion(),
      });
      return json(200, tenant);
    }
    if (seg.length === 3 && method === 'POST' && (seg[2] === 'suspend' || seg[2] === 'activate')) {
      tenant.status = seg[2] === 'suspend' ? 'suspended' : 'active';
      tenant.rowVersion = nextVersion();
      return json(200, tenant);
    }
    throw new HttpError(405, 'http.method_not_allowed');
  }

  function organizationRoutes(user: MockUser, data: MockTenantData, method: string, body: Record<string, unknown>) {
    if (method === 'GET') {
      demand(user, 'organization.read');
      return json(200, data.organization);
    }
    if (method === 'PUT') {
      demand(user, 'organization.manage');
      checkVersion(data.organization.rowVersion, body);
      const errors = required(body, ['name', 'defaultLocale', 'timeZone']);
      emailCheck(errors, body);
      assertValid(errors);
      data.organization = {
        name: String(body.name), legalName: (body.legalName as string) || null, phone: (body.phone as string) || null,
        email: (body.email as string) || null, address: (body.address as string) || null,
        defaultLocale: String(body.defaultLocale), timeZone: String(body.timeZone), rowVersion: nextVersion(),
      };
      return json(200, data.organization);
    }
    throw new HttpError(405, 'http.method_not_allowed');
  }

  function branchRoutes(user: MockUser, data: MockTenantData, seg: string[], method: string, url: URL, body: Record<string, unknown>) {
    if (seg.length === 1 && method === 'GET') {
      demand(user, 'branches.read');
      return json(200, paginate(data.branches, url, (b) => [b.name, b.code]));
    }
    if (seg.length === 1 && method === 'POST') {
      demand(user, 'branches.manage', null);
      const errors = required(body, ['code', 'name']);
      if (data.branches.some((b) => b.code === body.code)) errors.code = ['validation.duplicate'];
      assertValid(errors);
      const branch: Branch = {
        id: uuid(), code: String(body.code), name: String(body.name), address: (body.address as string) || null,
        phone: (body.phone as string) || null, isActive: body.isActive !== false, rowVersion: nextVersion(),
      };
      data.branches.push(branch);
      return json(201, branch);
    }
    const branch = data.branches.find((b) => b.id === seg[1]);
    if (!branch) throw new HttpError(404, 'resource.not_found');
    if (method === 'GET') {
      demand(user, 'branches.read');
      return json(200, branch);
    }
    if (method === 'PUT') {
      demand(user, 'branches.manage', branch.id);
      checkVersion(branch.rowVersion, body);
      const errors = required(body, ['code', 'name']);
      if (data.branches.some((b) => b.code === body.code && b.id !== branch.id)) errors.code = ['validation.duplicate'];
      assertValid(errors);
      Object.assign(branch, {
        code: body.code, name: body.name, address: (body.address as string) || null, phone: (body.phone as string) || null,
        isActive: body.isActive !== false, rowVersion: nextVersion(),
      });
      return json(200, branch);
    }
    if (method === 'DELETE') {
      demand(user, 'branches.manage', null);
      const inUse = PERSON_KINDS.some((k) => (data[k] as PersonBase[]).some((p) => p.branchId === branch.id));
      if (inUse) throw new HttpError(409, 'branch.in_use');
      data.branches = data.branches.filter((b) => b !== branch);
      return json(204, null);
    }
    throw new HttpError(405, 'http.method_not_allowed');
  }

  function roleRoutes(user: MockUser, data: MockTenantData, seg: string[], method: string, _url: URL, body: Record<string, unknown>) {
    const validate = (exceptId?: string) => {
      const errors = required(body, ['code', 'name']);
      if (data.roles.some((r) => r.code === body.code && r.id !== exceptId)) errors.code = ['validation.duplicate'];
      const known = new Set(PERMISSIONS.map((p) => p.code));
      if (Array.isArray(body.permissions) && body.permissions.some((p) => !known.has(String(p)))) {
        errors.permissions = ['validation.unknown_permission'];
      }
      assertValid(errors);
    };
    if (seg.length === 1 && method === 'GET') {
      demand(user, 'roles.read');
      return json(200, data.roles); // plain array (contract clarification)
    }
    if (seg.length === 1 && method === 'POST') {
      demand(user, 'roles.manage');
      validate();
      const role: Role = {
        id: uuid(), code: String(body.code), name: String(body.name), isSystem: false,
        permissions: ((body.permissions as string[]) ?? []).slice().sort(), rowVersion: nextVersion(),
      };
      data.roles.push(role);
      return json(201, role);
    }
    const role = data.roles.find((r) => r.id === seg[1]);
    if (!role) throw new HttpError(404, 'resource.not_found');
    if (method === 'GET') {
      demand(user, 'roles.read');
      return json(200, role);
    }
    if (method === 'PUT') {
      demand(user, 'roles.manage');
      checkVersion(role.rowVersion, body);
      validate(role.id);
      if (role.isSystem && body.code !== role.code) throw new HttpError(409, 'role.system_immutable');
      Object.assign(role, { code: body.code, name: body.name, permissions: ((body.permissions as string[]) ?? []).slice().sort(), rowVersion: nextVersion() });
      return json(200, role);
    }
    if (method === 'DELETE') {
      demand(user, 'roles.manage');
      if (role.isSystem) throw new HttpError(409, 'role.system_immutable');
      if (state.users.some((u) => u.assignments.some((a) => a.roleId === role.id))) throw new HttpError(409, 'role.in_use');
      data.roles = data.roles.filter((r) => r !== role);
      return json(204, null);
    }
    throw new HttpError(405, 'http.method_not_allowed');
  }

  /** User DTO per contract clarification: assignments carry roleCode. */
  function publicUser(u: MockUser) {
    const roles = state.data[u.tenantId ?? '']?.roles ?? [];
    return {
      id: u.id,
      userName: u.userName,
      displayName: u.displayName,
      email: u.email,
      phone: u.phone,
      isActive: u.isActive,
      locale: u.locale,
      timeZone: u.timeZone,
      assignments: u.assignments.map((a) => ({
        roleId: a.roleId,
        roleCode: roles.find((r) => r.id === a.roleId)?.code ?? '',
        branchId: a.branchId,
      })),
      rowVersion: u.rowVersion,
    };
  }

  function userRoutes(user: MockUser, data: MockTenantData, seg: string[], method: string, url: URL, body: Record<string, unknown>) {
    const tenantUsers = state.users.filter((u) => u.tenantId === user.tenantId);
    if (seg.length === 1 && method === 'GET') {
      demand(user, 'users.read');
      return json(200, paginate(tenantUsers.map(publicUser), url, (u) => [u.userName, u.displayName, u.email]));
    }
    if (seg.length === 1 && method === 'POST') {
      demand(user, 'users.manage');
      const errors = required(body, ['userName', 'displayName', 'password']);
      emailCheck(errors, body);
      if (typeof body.password === 'string' && body.password.length > 0 && body.password.length < 8) {
        errors.password = ['validation.min_length'];
      }
      if (tenantUsers.some((u) => u.userName.toLowerCase() === String(body.userName).toLowerCase())) errors.userName = ['validation.duplicate'];
      assertValid(errors);
      const created: MockUser = {
        id: uuid(), tenantId: user.tenantId, userName: String(body.userName), displayName: String(body.displayName),
        email: (body.email as string) || null, phone: (body.phone as string) || null, locale: (body.locale as string) || null,
        timeZone: (body.timeZone as string) || null, isActive: true, isPlatformAdmin: false, assignments: [], rowVersion: nextVersion(),
      };
      state.users.push(created);
      return json(201, publicUser(created));
    }
    const target = tenantUsers.find((u) => u.id === seg[1]);
    if (!target) throw new HttpError(404, 'resource.not_found');
    if (seg.length === 2 && method === 'GET') {
      demand(user, 'users.read');
      return json(200, publicUser(target));
    }
    if (seg.length === 2 && method === 'PUT') {
      demand(user, 'users.manage');
      checkVersion(target.rowVersion, body);
      const errors = required(body, ['displayName']);
      emailCheck(errors, body);
      assertValid(errors);
      Object.assign(target, {
        displayName: body.displayName, email: (body.email as string) || null, phone: (body.phone as string) || null,
        locale: (body.locale as string) || null, timeZone: (body.timeZone as string) || null, rowVersion: nextVersion(),
      });
      return json(200, publicUser(target));
    }
    if (seg.length === 3 && method === 'POST' && (seg[2] === 'activate' || seg[2] === 'deactivate')) {
      demand(user, 'users.manage');
      if (target.id === user.id && seg[2] === 'deactivate') throw new HttpError(409, 'user.cannot_deactivate_self');
      target.isActive = seg[2] === 'activate';
      target.rowVersion = nextVersion();
      return json(200, publicUser(target));
    }
    if (seg.length === 3 && method === 'PUT' && seg[2] === 'assignments') {
      demand(user, 'users.manage');
      const list = Array.isArray(body) ? (body as unknown as { roleId: string; branchId: string | null }[]) : null;
      if (!list) throw new HttpError(400, 'validation.failed', { errors: { assignments: ['validation.invalid_format'] } });
      for (const a of list) {
        if (!data.roles.some((r) => r.id === a.roleId)) throw new HttpError(400, 'validation.failed', { errors: { assignments: ['validation.unknown_role'] } });
        if (a.branchId !== null && !data.branches.some((b) => b.id === a.branchId)) {
          throw new HttpError(400, 'validation.failed', { errors: { assignments: ['validation.unknown_branch'] } });
        }
      }
      target.assignments = list.map((a) => ({ roleId: a.roleId, branchId: a.branchId ?? null }));
      target.rowVersion = nextVersion();
      return json(200, publicUser(target));
    }
    throw new HttpError(405, 'http.method_not_allowed');
  }

  function peopleRoutes(
    user: MockUser, data: MockTenantData, kind: PersonKind, seg: string[], method: string, url: URL, body: Record<string, unknown>,
  ) {
    const list = data[kind] as PersonBase[];
    const visible = (p: PersonBase) => {
      const allowed = branchesWith(user, `${kind}.read`);
      return allowed === null || allowed.has(p.branchId);
    };
    const validate = (existingId?: string) => {
      const extra = kind === 'students' ? [] : kind === 'teachers' ? ['employeeNumber'] : kind === 'staff' ? ['employeeNumber', 'position'] : [];
      const errors = required(body, ['branchId', 'firstName', 'lastName', ...extra]);
      emailCheck(errors, body);
      if (body.birthDate && !/^\d{4}-\d{2}-\d{2}$/.test(String(body.birthDate))) errors.birthDate = ['validation.invalid_date'];
      if (body.branchId && !data.branches.some((b) => b.id === body.branchId)) errors.branchId = ['validation.unknown_branch'];
      if (typeof body.nationalId === 'string' && body.nationalId && !/^\d{10}$/.test(body.nationalId)) errors.nationalId = ['validation.invalid_national_id'];
      if (body.gender && !GENDERS.includes(body.gender as Gender)) errors.gender = ['validation.invalid_format'];
      if (body.status && !PERSON_STATUSES.includes(body.status as PersonStatus)) errors.status = ['validation.invalid_format'];
      if (kind === 'students' && body.studentNumber && list.some((p) => (p as Student).studentNumber === body.studentNumber && p.id !== existingId)) {
        errors.studentNumber = ['validation.duplicate'];
      }
      assertValid(errors);
    };
    const fields = (): Partial<Student & Teacher & Staff & Guardian> => {
      const base: Partial<Student & Teacher & Staff> = {
        branchId: String(body.branchId), firstName: String(body.firstName), lastName: String(body.lastName),
        nationalId: (body.nationalId as string) || null, birthDate: (body.birthDate as string) || null,
        gender: (body.gender as Gender) || null, phone: (body.phone as string) || null, email: (body.email as string) || null,
        status: ((body.status as PersonStatus) || 'active'),
      };
      if (kind === 'students') {
        // Optional on create: generated server-side when omitted (contract clarification).
        base.studentNumber = body.studentNumber ? String(body.studentNumber) : nextStudentNumber(list as Student[]);
      }
      if (kind === 'teachers') {
        base.employeeNumber = String(body.employeeNumber);
        base.subjects = Array.isArray(body.subjects) ? (body.subjects as string[]) : null;
      }
      if (kind === 'staff') {
        base.employeeNumber = String(body.employeeNumber);
        base.position = String(body.position);
      }
      return base;
    };

    if (seg.length === 1 && method === 'GET') {
      if (branchesWith(user, `${kind}.read`)?.size === 0) throw new HttpError(403, 'auth.forbidden');
      return json(200, paginate(list.filter(visible), url, (p) => [
        p.firstName, p.lastName, `${p.firstName} ${p.lastName}`, p.nationalId, p.email, p.phone,
        (p as Student).studentNumber, (p as Teacher).employeeNumber,
      ]));
    }
    if (seg.length === 1 && method === 'POST') {
      demand(user, `${kind}.manage`, String(body.branchId ?? ''));
      validate();
      const created = { id: uuid(), userId: null, rowVersion: nextVersion(), ...fields(), ...(kind === 'students' ? { guardians: [] } : {}) } as PersonBase;
      list.push(created);
      return json(201, created);
    }
    const person = list.find((p) => p.id === seg[1]);
    // Out-of-scope records look exactly like missing ones (no existence leak).
    if (!person || !visible(person)) throw new HttpError(404, 'resource.not_found');

    if (kind === 'students' && seg[2] === 'guardians') {
      const student = person as Student;
      if (method === 'GET') return json(200, student.guardians);
      if (method === 'PUT') {
        demand(user, 'students.manage', student.branchId);
        const links = Array.isArray(body) ? (body as unknown as Student['guardians']) : null;
        if (!links || links.some((l) => !data.guardians.some((g) => g.id === l.guardianId))) {
          throw new HttpError(400, 'validation.failed', { errors: { guardians: ['validation.unknown_guardian'] } });
        }
        if (links.filter((l) => l.isPrimary).length > 1) {
          throw new HttpError(400, 'validation.failed', { errors: { guardians: ['validation.single_primary_guardian'] } });
        }
        student.guardians = links.map((l) => ({ guardianId: l.guardianId, relationship: l.relationship, isPrimary: !!l.isPrimary }));
        student.rowVersion = nextVersion();
        return json(200, student.guardians);
      }
    }
    if (seg.length === 2 && method === 'GET') return json(200, person);
    if (seg.length === 2 && method === 'PUT') {
      demand(user, `${kind}.manage`, person.branchId);
      checkVersion(person.rowVersion, body);
      validate(person.id);
      demand(user, `${kind}.manage`, String(body.branchId)); // moving to another branch needs scope there too
      Object.assign(person, fields(), { rowVersion: nextVersion() });
      return json(200, person);
    }
    if (seg.length === 2 && method === 'DELETE') {
      demand(user, `${kind}.manage`, person.branchId);
      (data as unknown as Record<PersonKind, PersonBase[]>)[kind] = list.filter((p) => p !== person);
      return json(204, null);
    }
    throw new HttpError(405, 'http.method_not_allowed');
  }

  function portalRoutes(user: MockUser, data: MockTenantData, seg: string[]) {
    const own = <T extends PersonBase>(kind: PersonKind) => (data[kind] as T[]).find((p) => p.userId === user.id);
    if (seg[1] === 'me' && seg[2] === 'profile') {
      const student = own<Student>('students');
      if (student) return json(200, { kind: 'student', profile: student });
      const guardian = own<Guardian>('guardians');
      if (guardian) return json(200, { kind: 'guardian', profile: guardian });
      const teacher = own<Teacher>('teachers');
      if (teacher) return json(200, { kind: 'teacher', profile: teacher });
      const staff = own<Staff>('staff');
      if (staff) return json(200, { kind: 'staff', profile: staff });
      throw new HttpError(404, 'portal.profile_not_linked');
    }
    if (seg[1] === 'me' && seg[2] === 'students') {
      const guardian = own<Guardian>('guardians');
      if (!guardian) throw new HttpError(403, 'auth.forbidden');
      return json(200, data.students.filter((s) => s.guardians.some((g) => g.guardianId === guardian.id)));
    }
    throw new HttpError(404, 'resource.not_found');
  }

  const mockFetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request && !init ? input : new Request(input, init);
    const traceId = `mock-${randomToken().slice(0, 16)}`;
    const correlation = request.headers.get('x-correlation-id');
    let response: Response;
    try {
      response = await handle(request);
    } catch (error) {
      response =
        error instanceof HttpError ? problem(error, traceId) : problem(new HttpError(500, 'server.error'), traceId);
    }
    if (correlation) response.headers.set('X-Correlation-Id', correlation);
    return response;
  }) as typeof fetch;

  return {
    fetch: mockFetch,
    reset() {
      state = createSeed();
      access = new Map();
      refresh = new Map();
    },
    state: () => state,
    touch(kind, tenantSlug, entityId) {
      const tenant = state.tenants.find((t) => t.slug === tenantSlug);
      const data = tenant ? state.data[tenant.id] : undefined;
      if (!data) return;
      if (kind === 'organization') data.organization.rowVersion = nextVersion();
      else {
        const branch = data.branches.find((b) => b.id === entityId);
        if (branch) branch.rowVersion = nextVersion();
      }
    },
  };
}

function nextStudentNumber(students: Student[]): string {
  const max = students.reduce((acc, s) => {
    const n = Number(/(\d+)$/.exec(s.studentNumber ?? '')?.[1] ?? 0);
    return Number.isFinite(n) && n > acc ? n : acc;
  }, 1000);
  return `ST-${max + 1}`;
}

function isValidTimeZone(zone: string): boolean {
  try {
    new Intl.DateTimeFormat('en', { timeZone: zone });
    return true;
  } catch {
    return false;
  }
}
