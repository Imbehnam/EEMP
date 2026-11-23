/**
 * Synthetic development data for mock mode. Names are fictional; no real personal data.
 * Mirrors the contract's development seed: platform.admin, tenant "demo" with demo.admin,
 * two branches, sample roles (TenantAdmin, BranchManager, Teacher, Guardian, Student) and a few people.
 * A second tenant ("alborz") exists only to exercise tenant isolation.
 */
import type { Branch, Guardian, Organization, Permission, Role, Staff, Student, Teacher, Tenant } from '../types';

export interface MockUser {
  id: string;
  tenantId: string | null;
  userName: string;
  displayName: string;
  email: string | null;
  phone: string | null;
  locale: string | null;
  timeZone: string | null;
  isActive: boolean;
  isPlatformAdmin: boolean;
  assignments: { roleId: string; branchId: string | null }[];
  rowVersion: string;
}

export interface MockTenantData {
  organization: Organization;
  branches: Branch[];
  roles: Role[];
  students: Student[];
  guardians: Guardian[];
  teachers: Teacher[];
  staff: Staff[];
}

export interface MockState {
  tenants: Tenant[];
  users: MockUser[];
  data: Record<string, MockTenantData>;
  version: number;
}

export const MODULES = ['organization', 'branches', 'roles', 'users', 'students', 'guardians', 'teachers', 'staff'] as const;

export const PERMISSIONS: Permission[] = MODULES.flatMap((module) =>
  module === 'organization'
    ? [
        { code: 'organization.read', module },
        { code: 'organization.manage', module },
      ]
    : [
        { code: `${module}.read`, module },
        { code: `${module}.manage`, module },
      ],
);

const ALL = PERMISSIONS.map((p) => p.code);
const PEOPLE = ALL.filter((c) => /^(students|guardians|teachers|staff)\./.test(c));
const TENANT_CONFIG = ALL.filter((c) => !PEOPLE.includes(c));

export const IDS = {
  tenantDemo: '6f1c2a10-0000-4000-8000-000000000001',
  tenantAlborz: '6f1c2a10-0000-4000-8000-000000000002',
  branchNorth: 'b0000000-0000-4000-8000-000000000001',
  branchSouth: 'b0000000-0000-4000-8000-000000000002',
  branchAlborz: 'b0000000-0000-4000-8000-000000000003',
  roleTenantAdmin: 'a0000000-0000-4000-8000-000000000001',
  roleBranchManager: 'a0000000-0000-4000-8000-000000000002',
  roleTeacher: 'a0000000-0000-4000-8000-000000000003',
  roleGuardian: 'a0000000-0000-4000-8000-000000000004',
  roleStudent: 'a0000000-0000-4000-8000-000000000005',
  roleAlborzAdmin: 'a0000000-0000-4000-8000-000000000006',
  userPlatform: 'c0000000-0000-4000-8000-000000000001',
  userDemoAdmin: 'c0000000-0000-4000-8000-000000000002',
  userTeacher: 'c0000000-0000-4000-8000-000000000003',
  userGuardian: 'c0000000-0000-4000-8000-000000000004',
  userStudent: 'c0000000-0000-4000-8000-000000000005',
  userManagerSouth: 'c0000000-0000-4000-8000-000000000006',
  userAlborzAdmin: 'c0000000-0000-4000-8000-000000000007',
  student1: 'd0000000-0000-4000-8000-000000000001',
  student2: 'd0000000-0000-4000-8000-000000000002',
  student3: 'd0000000-0000-4000-8000-000000000003',
  guardian1: 'd0000000-0000-4000-8000-000000000011',
  teacher1: 'd0000000-0000-4000-8000-000000000021',
  staff1: 'd0000000-0000-4000-8000-000000000031',
} as const;

export function createSeed(): MockState {
  let v = 0;
  const rv = () => btoa(`v${++v}`);
  const created = '2026-01-15T08:30:00Z';

  const tenants: Tenant[] = [
    {
      id: IDS.tenantDemo,
      slug: 'demo',
      name: 'Demo Institute Group',
      defaultLocale: 'fa',
      supportedLocales: ['fa', 'en', 'tr', 'ar'],
      timeZone: 'Asia/Tehran',
      currency: 'IRR',
      status: 'Active',
      createdAt: created,
      rowVersion: rv(),
    },
    {
      id: IDS.tenantAlborz,
      slug: 'alborz',
      name: 'Alborz Academy',
      defaultLocale: 'fa',
      supportedLocales: ['fa', 'en'],
      timeZone: 'Asia/Tehran',
      currency: 'IRR',
      status: 'Active',
      createdAt: created,
      rowVersion: rv(),
    },
  ];

  const role = (id: string, code: string, name: string, permissions: string[]): Role => ({
    id,
    code,
    name,
    isSystem: true,
    permissions,
    rowVersion: rv(),
  });

  const person = <T extends object>(base: T) => ({ status: 'Active', userId: null, rowVersion: rv(), ...base });

  const demo: MockTenantData = {
    organization: {
      name: 'Demo Institute Group',
      legalName: 'Demo Education Co.',
      phone: '+982100000000',
      email: 'office@demo.example',
      address: 'Tehran',
      defaultLocale: 'fa',
      timeZone: 'Asia/Tehran',
      rowVersion: rv(),
    },
    branches: [
      { id: IDS.branchNorth, code: 'N01', name: 'North Branch', address: 'Tehran, North', phone: '+982100000001', isActive: true, rowVersion: rv() },
      { id: IDS.branchSouth, code: 'S01', name: 'South Branch', address: 'Tehran, South', phone: '+982100000002', isActive: true, rowVersion: rv() },
    ],
    roles: [
      // Tenant admins manage configuration only — no implicit access to people records (security baseline #1).
      role(IDS.roleTenantAdmin, 'TenantAdmin', 'Tenant administrator', TENANT_CONFIG),
      role(IDS.roleBranchManager, 'BranchManager', 'Branch manager', ['branches.read', ...PEOPLE]),
      role(IDS.roleTeacher, 'Teacher', 'Teacher', ['students.read']),
      role(IDS.roleGuardian, 'Guardian', 'Guardian', []),
      role(IDS.roleStudent, 'Student', 'Student', []),
    ],
    students: [
      person({
        id: IDS.student1, branchId: IDS.branchNorth, firstName: 'رضا', lastName: 'احمدی', nationalId: '0012345678',
        birthDate: '2012-04-03', gender: 'Male', phone: null, email: null, studentNumber: 'ST-1001',
        userId: IDS.userStudent, guardians: [{ guardianId: IDS.guardian1, relationship: 'Father', isPrimary: true }],
      }) as Student,
      person({
        id: IDS.student2, branchId: IDS.branchNorth, firstName: 'Sara', lastName: 'Ahmadi', nationalId: null,
        birthDate: '2014-09-21', gender: 'Female', phone: null, email: null, studentNumber: 'ST-1002',
        guardians: [{ guardianId: IDS.guardian1, relationship: 'Father', isPrimary: true }],
      }) as Student,
      person({
        id: IDS.student3, branchId: IDS.branchSouth, firstName: 'Mehmet', lastName: 'Yılmaz', nationalId: null,
        birthDate: '2011-01-10', gender: 'Male', phone: null, email: null, studentNumber: 'ST-2001', guardians: [],
      }) as Student,
    ],
    guardians: [
      person({
        id: IDS.guardian1, branchId: IDS.branchNorth, firstName: 'علی', lastName: 'احمدی', nationalId: null,
        birthDate: null, gender: 'Male', phone: '+989120000000', email: 'guardian@demo.example', userId: IDS.userGuardian,
      }) as Guardian,
    ],
    teachers: [
      person({
        id: IDS.teacher1, branchId: IDS.branchNorth, firstName: 'سارا', lastName: 'کریمی', nationalId: null,
        birthDate: null, gender: 'Female', phone: null, email: 'teacher@demo.example', employeeNumber: 'T-01',
        subjects: ['Mathematics', 'Physics'], userId: IDS.userTeacher,
      }) as Teacher,
    ],
    staff: [
      person({
        id: IDS.staff1, branchId: IDS.branchSouth, firstName: 'Layla', lastName: 'Hassan', nationalId: null,
        birthDate: null, gender: 'Female', phone: null, email: null, employeeNumber: 'S-01', position: 'Registrar',
      }) as Staff,
    ],
  };

  const alborz: MockTenantData = {
    organization: {
      name: 'Alborz Academy', legalName: null, phone: null, email: null, address: null,
      defaultLocale: 'fa', timeZone: 'Asia/Tehran', rowVersion: rv(),
    },
    branches: [{ id: IDS.branchAlborz, code: 'A01', name: 'Karaj', address: null, phone: null, isActive: true, rowVersion: rv() }],
    roles: [role(IDS.roleAlborzAdmin, 'TenantAdmin', 'Tenant administrator', TENANT_CONFIG)],
    students: [], guardians: [], teachers: [], staff: [],
  };

  const user = (u: Omit<MockUser, 'rowVersion' | 'email' | 'phone' | 'isActive'> & Partial<MockUser>): MockUser => ({
    email: null, phone: null, isActive: true, rowVersion: rv(), ...u,
  });

  const users: MockUser[] = [
    user({ id: IDS.userPlatform, tenantId: null, userName: 'platform.admin', displayName: 'Platform Administrator', locale: 'en', timeZone: 'UTC', isPlatformAdmin: true, assignments: [] }),
    user({
      id: IDS.userDemoAdmin, tenantId: IDS.tenantDemo, userName: 'demo.admin', displayName: 'مدیر دمو', locale: 'fa', timeZone: 'Asia/Tehran', isPlatformAdmin: false,
      assignments: [{ roleId: IDS.roleTenantAdmin, branchId: null }, { roleId: IDS.roleBranchManager, branchId: IDS.branchNorth }],
    }),
    user({ id: IDS.userManagerSouth, tenantId: IDS.tenantDemo, userName: 'manager.south', displayName: 'South Manager', locale: 'en', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleBranchManager, branchId: IDS.branchSouth }] }),
    user({ id: IDS.userTeacher, tenantId: IDS.tenantDemo, userName: 'teacher.sara', displayName: 'سارا کریمی', locale: 'fa', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleTeacher, branchId: IDS.branchNorth }] }),
    user({ id: IDS.userGuardian, tenantId: IDS.tenantDemo, userName: 'guardian.ali', displayName: 'علی احمدی', locale: 'fa', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleGuardian, branchId: IDS.branchNorth }] }),
    user({ id: IDS.userStudent, tenantId: IDS.tenantDemo, userName: 'student.reza', displayName: 'رضا احمدی', locale: 'fa', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleStudent, branchId: IDS.branchNorth }] }),
    user({ id: IDS.userAlborzAdmin, tenantId: IDS.tenantAlborz, userName: 'alborz.admin', displayName: 'Alborz Admin', locale: 'fa', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleAlborzAdmin, branchId: null }] }),
  ];

  return { tenants, users, data: { [IDS.tenantDemo]: demo, [IDS.tenantAlborz]: alborz }, version: v };
}
