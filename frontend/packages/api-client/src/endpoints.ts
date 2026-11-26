import { createAcademicEndpoints } from './academic-endpoints';
import { createFinanceEndpoints } from './finance-endpoints';
import { createWorkflowEndpoints } from './workflow-endpoints';
import type { ApiClient } from './client';
import type {
  Branch,
  BranchInput,
  CreateTenantRequest,
  CreateUserRequest,
  CurrentUser,
  Guid,
  Organization,
  PageQuery,
  Paged,
  Permission,
  PersonByKind,
  PersonInput,
  PersonKind,
  PersonUpdate,
  PortalProfile,
  PortalStudent,
  PreferencesRequest,
  Role,
  RoleInput,
  StudentGuardianDetail,
  StudentGuardianLink,
  Tenant,
  TenantUser,
  UpdateBranchRequest,
  UpdateOrganizationRequest,
  UpdateRoleRequest,
  UpdateTenantRequest,
  UpdateUserRequest,
  UserAssignment,
} from './types';

/**
 * Paged lists (tenants, users, branches, people) use `{ items, page, pageSize, totalCount }`.
 * Defensive normalizer in case a collection is returned as a plain array.
 */
export function toPaged<T>(value: Paged<T> | T[] | null | undefined, query: PageQuery = {}): Paged<T> {
  if (Array.isArray(value)) {
    return { items: value, page: 1, pageSize: value.length || query.pageSize || 0, totalCount: value.length };
  }
  if (!value) return { items: [], page: 1, pageSize: query.pageSize ?? 0, totalCount: 0 };
  return value;
}

const q = (query: PageQuery = {}) => ({ page: query.page, pageSize: query.pageSize, search: query.search });
const id = (value: Guid) => encodeURIComponent(value);

/** Typed endpoint map for every Milestone 1 and Milestone 2 contract operation (paths relative to /api/v1). */
export function createEndpoints(client: ApiClient) {
  return {
    auth: {
      /** Token-bearing auth calls (login/refresh/logout) are BFF-only; see @eemp/api-client/server. */
      me: () => client.get<CurrentUser>('/auth/me'),
      updatePreferences: (body: PreferencesRequest) => client.put<void>('/auth/me/preferences', body),
    },
    platform: {
      tenants: {
        list: async (query?: PageQuery) =>
          toPaged(await client.get<Paged<Tenant> | Tenant[]>('/platform/tenants', { query: q(query) }), query),
        get: (tenantId: Guid) => client.get<Tenant>(`/platform/tenants/${id(tenantId)}`),
        create: (body: CreateTenantRequest) => client.post<Tenant>('/platform/tenants', body),
        update: (tenantId: Guid, body: UpdateTenantRequest) =>
          client.put<Tenant>(`/platform/tenants/${id(tenantId)}`, body),
        suspend: (tenantId: Guid) => client.post<Tenant | void>(`/platform/tenants/${id(tenantId)}/suspend`),
        activate: (tenantId: Guid) => client.post<Tenant | void>(`/platform/tenants/${id(tenantId)}/activate`),
      },
    },
    organization: {
      get: () => client.get<Organization>('/organization'),
      update: (body: UpdateOrganizationRequest) => client.put<Organization>('/organization', body),
    },
    branches: {
      list: async (query?: PageQuery) =>
        toPaged(await client.get<Paged<Branch> | Branch[]>('/branches', { query: q(query) }), query),
      get: (branchId: Guid) => client.get<Branch>(`/branches/${id(branchId)}`),
      create: (body: BranchInput) => client.post<Branch>('/branches', body),
      update: (branchId: Guid, body: UpdateBranchRequest) => client.put<Branch>(`/branches/${id(branchId)}`, body),
      remove: (branchId: Guid) => client.delete(`/branches/${id(branchId)}`),
    },
    permissions: {
      list: () => client.get<Permission[]>('/permissions'),
    },
    roles: {
      /** Plain array (not paged) per contract clarification. */
      list: () => client.get<Role[]>('/roles'),
      get: (roleId: Guid) => client.get<Role>(`/roles/${id(roleId)}`),
      create: (body: RoleInput) => client.post<Role>('/roles', body),
      update: (roleId: Guid, body: UpdateRoleRequest) => client.put<Role>(`/roles/${id(roleId)}`, body),
      remove: (roleId: Guid) => client.delete(`/roles/${id(roleId)}`),
    },
    users: {
      list: async (query?: PageQuery) =>
        toPaged(await client.get<Paged<TenantUser> | TenantUser[]>('/users', { query: q(query) }), query),
      get: (userId: Guid) => client.get<TenantUser>(`/users/${id(userId)}`),
      create: (body: CreateUserRequest) => client.post<TenantUser>('/users', body),
      update: (userId: Guid, body: UpdateUserRequest) => client.put<TenantUser>(`/users/${id(userId)}`, body),
      deactivate: (userId: Guid) => client.post<TenantUser | void>(`/users/${id(userId)}/deactivate`),
      activate: (userId: Guid) => client.post<TenantUser | void>(`/users/${id(userId)}/activate`),
      setAssignments: (userId: Guid, body: UserAssignment[]) =>
        client.put<TenantUser | void>(`/users/${id(userId)}/assignments`, body),
    },
    people: <K extends PersonKind>(kind: K) => ({
      list: async (query?: PageQuery) =>
        toPaged(await client.get<Paged<PersonByKind[K]> | PersonByKind[K][]>(`/${kind}`, { query: q(query) }), query),
      get: (personId: Guid) => client.get<PersonByKind[K]>(`/${kind}/${id(personId)}`),
      create: (body: PersonInput<K>) => client.post<PersonByKind[K]>(`/${kind}`, body),
      update: (personId: Guid, body: PersonUpdate<K>) => client.put<PersonByKind[K]>(`/${kind}/${id(personId)}`, body),
      remove: (personId: Guid) => client.delete(`/${kind}/${id(personId)}`),
    }),
    studentGuardians: {
      get: (studentId: Guid) => client.get<StudentGuardianDetail[]>(`/students/${id(studentId)}/guardians`),
      set: (studentId: Guid, body: StudentGuardianLink[]) =>
        client.put<StudentGuardianDetail[]>(`/students/${id(studentId)}/guardians`, body),
    },
    portal: {
      profile: () => client.get<PortalProfile>('/portal/me/profile'),
      /** Plain array (not paged) per contract clarification. */
      students: () => client.get<PortalStudent[]>('/portal/me/students'),
    },
    /** Milestone 2 academic core (EEMP-008..010). */
    academic: createAcademicEndpoints(client),
    /** Milestone 3 institute finance (EEMP-014). */
    finance: createFinanceEndpoints(client),
    /** Workflow engine (EEMP-011). */
    workflow: createWorkflowEndpoints(client),
  };
}

export type Endpoints = ReturnType<typeof createEndpoints>;
