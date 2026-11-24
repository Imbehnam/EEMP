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

/** Enums are camelCase strings (contract clarification 2026-11-24). */
export type TenantStatus = 'active' | 'suspended' | (string & {});

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
  /** `<module>.read` or `<module>.manage`. */
  code: string;
  module: string;
  /** Grants access to sensitive personal data; the role editor highlights it. */
  isSensitive: boolean;
  /** Evaluated per branch assignment (vs. organization-wide only). */
  branchScoped: boolean;
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
/** The role code is immutable after creation; PUT /roles/{id} takes name + permissions only. */
export interface UpdateRoleRequest {
  name: string;
  permissions: string[];
  rowVersion: RowVersion;
}

/** Assignment as sent to PUT /users/{id}/assignments. */
export interface UserAssignment {
  roleId: Guid;
  branchId: Guid | null;
}

/** Assignment as returned on the user DTO. */
export interface UserAssignmentView extends UserAssignment {
  roleCode: string;
}

/** User DTO (contract clarification 2026-11-24). */
export interface TenantUser {
  id: Guid;
  userName: string;
  displayName: string;
  email: string | null;
  phone: string | null;
  isActive: boolean;
  locale: string | null;
  timeZone: string | null;
  assignments: UserAssignmentView[];
  createdAt?: UtcInstant;
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

export type Gender = 'male' | 'female' | 'other';
export const GENDERS: readonly Gender[] = ['male', 'female', 'other'];
export type PersonStatus = 'active' | 'inactive';
export const PERSON_STATUSES: readonly PersonStatus[] = ['active', 'inactive'];
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

export type GuardianRelationship = 'father' | 'mother' | 'legalGuardian' | 'grandparent' | 'sibling' | 'other';
export const GUARDIAN_RELATIONSHIPS: readonly GuardianRelationship[] = [
  'father', 'mother', 'legalGuardian', 'grandparent', 'sibling', 'other',
];

export interface StudentGuardianLink {
  guardianId: Guid;
  relationship: GuardianRelationship | (string & {});
  isPrimary: boolean;
}

/** GET /students/{id}/guardians item: the link plus guardian details (blank when the caller cannot read that guardian). */
export interface StudentGuardianDetail extends StudentGuardianLink {
  firstName: string;
  lastName: string;
  phone: string | null;
  branchId: Guid;
}

export interface Student extends PersonBase {
  studentNumber: string;
  guardians: StudentGuardianLink[];
}

export type Guardian = PersonBase;

export interface Teacher extends PersonBase {
  employeeNumber: string;
  /** Free text (backend stores one string, max 500), e.g. "Mathematics, Physics". Settled 2026-11-24. */
  subjects?: string | null;
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

/**
 * Create payload: everything except server-owned fields. Student guardian links are managed via
 * /students/{id}/guardians, and studentNumber is optional (server generates it when omitted).
 */
export interface PersonInputByKind {
  students: Omit<Student, 'id' | 'rowVersion' | 'guardians' | 'studentNumber'> & { studentNumber?: string | null };
  guardians: Omit<Guardian, 'id' | 'rowVersion'>;
  teachers: Omit<Teacher, 'id' | 'rowVersion'>;
  staff: Omit<Staff, 'id' | 'rowVersion'>;
}
export type PersonInput<K extends PersonKind> = PersonInputByKind[K];
export type PersonUpdate<K extends PersonKind> = PersonInput<K> & { rowVersion: RowVersion };

/* ---------- portal ---------- */

/** GET /portal/me/profile (contract clarification 2026-11-24). */
export type PortalProfile =
  | { kind: 'student'; profile: Student }
  | { kind: 'guardian'; profile: Guardian }
  | { kind: 'teacher'; profile: Teacher }
  | { kind: 'staff'; profile: Staff };
export type PortalKind = PortalProfile['kind'];

/** GET /portal/me/students item: a linked-student summary (not the full record). Settled 2026-11-24. */
export interface PortalStudent {
  id: Guid;
  branchId: Guid;
  firstName: string;
  lastName: string;
  studentNumber: string;
  relationship: GuardianRelationship | (string & {});
  isPrimary: boolean;
}
