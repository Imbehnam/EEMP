/**
 * DTOs for the EEMP Milestone 1 API contract (/api/v1, JSON camelCase).
 * Source of truth: the backend OpenAPI document at /openapi/v1.json.
 * Fields the shared contract leaves unspecified are marked "assumed" and listed in
 * docs/05-experience/FRONTEND_IMPLEMENTATION.md so they can be reconciled with OpenAPI.
 */

export type Guid = string;
/** ISO-8601 UTC instant. */
export type UtcInstant = string;
/** Calendar date yyyy-mm-dd (no time zone). */
export type IsoDate = string;
/** Opaque optimistic-concurrency token (base64). */
export type RowVersion = string;

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PageQuery {
  page?: number;
  pageSize?: number;
  search?: string;
}

export const MAX_PAGE_SIZE = 100;

/* ---------- auth ---------- */

export interface LoginRequest {
  /** null for platform administrators. */
  tenantSlug: string | null;
  userName: string;
  password: string;
}

export interface ScopeAssignment {
  roleCode: string;
  branchId: Guid | null;
}

export interface CurrentUser {
  id: Guid;
  userName: string;
  displayName: string;
  tenantId: Guid | null;
  tenantName: string | null;
  locale: string;
  timeZone: string;
  isPlatformAdmin: boolean;
  permissions: string[];
  scopes: ScopeAssignment[];
}

export interface TokenResponse {
  accessToken: string;
  accessTokenExpiresAt: UtcInstant;
  refreshToken: string;
  user: CurrentUser;
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface PreferencesRequest {
  locale: string;
  timeZone: string;
}

/* ---------- platform ---------- */

export type TenantStatus = 'Provisioning' | 'Active' | 'Suspended' | (string & {});

export interface Tenant {
  id: Guid;
  slug: string;
  name: string;
  defaultLocale: string;
  supportedLocales: string[];
  timeZone: string;
  currency: string;
  status: TenantStatus;
  createdAt: UtcInstant;
  rowVersion: RowVersion;
}

export interface TenantAdminUserInput {
  userName: string;
  displayName: string;
  email: string;
  phone: string;
  password: string;
}

export interface CreateTenantRequest {
  slug: string;
  name: string;
  defaultLocale: string;
  supportedLocales: string[];
  timeZone: string;
  currency: string;
  adminUser: TenantAdminUserInput;
}

export interface UpdateTenantRequest {
  name: string;
  defaultLocale: string;
  supportedLocales: string[];
  timeZone: string;
  currency: string;
  rowVersion: RowVersion;
}

/* ---------- organization & branches ---------- */

export interface Organization {
  name: string;
  legalName: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
  defaultLocale: string;
  timeZone: string;
  rowVersion: RowVersion;
}

export type UpdateOrganizationRequest = Organization;

export interface Branch {
  id: Guid;
  code: string;
  name: string;
  address: string | null;
  phone: string | null;
  isActive: boolean;
  rowVersion: RowVersion;
}

export type BranchInput = Omit<Branch, 'id' | 'rowVersion'>;
export type UpdateBranchRequest = BranchInput & { rowVersion: RowVersion };

/* ---------- authorization ---------- */

export interface Permission {
  code: string;
  module: string;
}

export interface Role {
  id: Guid;
  code: string;
  name: string;
  isSystem: boolean;
  permissions: string[];
  rowVersion: RowVersion;
}

export interface RoleInput {
  code: string;
  name: string;
  permissions: string[];
}
export type UpdateRoleRequest = RoleInput & { rowVersion: RowVersion };

export interface UserAssignment {
  roleId: Guid;
  branchId: Guid | null;
}

/** Assumed shape: the contract names the /users endpoints but not the user DTO fields. */
export interface TenantUser {
  id: Guid;
  userName: string;
  displayName: string;
  email: string | null;
  phone: string | null;
  locale: string | null;
  timeZone: string | null;
  isActive: boolean;
  assignments: UserAssignment[];
  rowVersion: RowVersion;
}

export interface CreateUserRequest {
  userName: string;
  displayName: string;
  email: string | null;
  phone: string | null;
  password: string;
  locale: string | null;
  timeZone: string | null;
}

export interface UpdateUserRequest {
  displayName: string;
  email: string | null;
  phone: string | null;
  locale: string | null;
  timeZone: string | null;
  rowVersion: RowVersion;
}

/* ---------- people ---------- */

export type Gender = 'Male' | 'Female' | (string & {});
export type PersonStatus = 'Active' | 'Inactive' | (string & {});
export type PersonKind = 'students' | 'guardians' | 'teachers' | 'staff';
export const PERSON_KINDS: readonly PersonKind[] = ['students', 'guardians', 'teachers', 'staff'];

export interface PersonBase {
  id: Guid;
  branchId: Guid;
  firstName: string;
  lastName: string;
  nationalId?: string | null;
  birthDate?: IsoDate | null;
  gender?: Gender | null;
  phone?: string | null;
  email?: string | null;
  status: PersonStatus;
  userId?: Guid | null;
  rowVersion: RowVersion;
}

export interface StudentGuardianLink {
  guardianId: Guid;
  relationship: string;
  isPrimary: boolean;
}

export interface Student extends PersonBase {
  studentNumber: string;
  guardians: StudentGuardianLink[];
}

export type Guardian = PersonBase;

export interface Teacher extends PersonBase {
  employeeNumber: string;
  subjects?: string[] | null;
}

export interface Staff extends PersonBase {
  employeeNumber: string;
  position: string;
}

export interface PersonByKind {
  students: Student;
  guardians: Guardian;
  teachers: Teacher;
  staff: Staff;
}

/** Create payload: everything except server-owned fields. */
export type PersonInput<K extends PersonKind> = Omit<PersonByKind[K], 'id' | 'rowVersion'>;
export type PersonUpdate<K extends PersonKind> = PersonInput<K> & { rowVersion: RowVersion };

/* ---------- portal ---------- */

/** Assumed shape: own person record plus its kind (the contract says "sees own record"). */
export type PortalProfile =
  | ({ kind: 'student' } & Student)
  | ({ kind: 'guardian' } & Guardian)
  | ({ kind: 'teacher' } & Teacher)
  | ({ kind: 'staff' } & Staff);
