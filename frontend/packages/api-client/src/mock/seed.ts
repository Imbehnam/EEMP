/**
 * Synthetic development data for mock mode. Names are fictional; no real personal data.
 * Mirrors the contract's development seed: platform.admin, tenant "demo" with demo.admin,
 * two branches, sample roles (TenantAdmin, BranchManager, Teacher, Guardian, Student) and a few people.
 * A second tenant ("alborz") exists only to exercise tenant isolation.
 */
import type { Branch, Guardian, Organization, Permission, Role, Staff, Student, Teacher, Tenant } from '../types';
import { ACADEMIC_IDS, createAcademicSeed, emptyAcademic, type MockAcademic } from './academic';
import { createFinanceSeed, emptyFinance, type MockFinance } from './finance';
import { createConsultationSeed, emptyConsultation, type MockConsultation } from './consultation';
import { createWorkflowSeed, emptyWorkflow, type MockWorkflow } from './workflow';

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
  /** Milestone 2 academic data (catalog, classes, enrollments, attendance, grades, report cards). */
  academic: MockAcademic;
  /** Milestone 3 finance data (fees, invoices, payments, refunds). */
  finance: MockFinance;
  /** EEMP-011 workflow definitions and instances. */
  workflow: MockWorkflow;
  /** EEMP-012 consultation booking and support tickets. */
  consultation: MockConsultation;
}

export interface MockState {
  tenants: Tenant[];
  users: MockUser[];
  data: Record<string, MockTenantData>;
  version: number;
}

export const MODULES = ['organization', 'branches', 'roles', 'users', 'students', 'guardians', 'teachers', 'staff'] as const;

/** People modules hold personal data of minors/guardians (sensitive) and are evaluated per branch. */
const SENSITIVE_MODULES = new Set(['students', 'guardians', 'teachers', 'staff']);
const BRANCH_SCOPED_MODULES = new Set(['branches', 'students', 'guardians', 'teachers', 'staff']);

/** Module group as the real API reports it (several resources share one module, e.g. people). */
const MODULE_GROUP: Record<(typeof MODULES)[number], string> = {
  organization: 'organization', branches: 'organization', roles: 'identity', users: 'identity',
  students: 'people', guardians: 'people', teachers: 'people', staff: 'people',
};

const M1_PERMISSIONS: Permission[] = MODULES.flatMap((module) =>
  (['read', 'manage'] as const).map((action) => ({
    code: `${module}.${action}`,
    module: MODULE_GROUP[module],
    // Granting role/user management can escalate privileges, so it is flagged as sensitive too.
    isSensitive: SENSITIVE_MODULES.has(module) || ((module === 'roles' || module === 'users') && action === 'manage'),
    branchScoped: BRANCH_SCOPED_MODULES.has(module),
  })),
);

/** Milestone 2 academic permissions (contract §1): module `academic`; catalog is organization-wide and not sensitive. */
const academic = (code: string, isSensitive: boolean, branchScoped: boolean): Permission => ({ code, module: 'academic', isSensitive, branchScoped });
export const ACADEMIC_PERMISSIONS: Permission[] = [
  academic('catalog.read', false, false),
  academic('catalog.manage', false, false),
  academic('classes.read', false, true),
  academic('classes.manage', false, true),
  ...['enrollments', 'attendance'].flatMap((r) => [academic(`${r}.read`, true, true), academic(`${r}.manage`, true, true)]),
  ...['read', 'manage', 'approve', 'publish'].map((a) => academic(`grades.${a}`, true, true)),
  ...['read', 'manage', 'publish'].map((a) => academic(`reportcards.${a}`, true, true)),
];

/** Milestone 3 finance permissions (contract §2): all sensitive and branch-scoped except fee setup. */
const fin = (code: string, isSensitive: boolean, branchScoped: boolean): Permission => ({ code, module: 'finance', isSensitive, branchScoped });
export const FINANCE_PERMISSIONS: Permission[] = [
  fin('finance.setup.manage', false, false),
  fin('finance.read', true, true),
  fin('finance.invoices.manage', true, true),
  fin('finance.payments.manage', true, true),
  fin('finance.approve', true, true),
  fin('finance.reports.read', true, true),
];
/** EEMP-011 workflow permissions (DL-072). */
const wfp = (code: string, isSensitive: boolean, branchScoped: boolean): Permission => ({ code, module: 'workflow', isSensitive, branchScoped });
export const WORKFLOW_PERMISSIONS: Permission[] = [
  wfp('workflow.definitions.read', false, false),
  wfp('workflow.definitions.manage', false, false),
  wfp('workflow.instances.read', true, true),
  wfp('workflow.instances.manage', true, true),
  wfp('workflow.tasks.approve', true, true),
];

/** EEMP-012 consultation and support permissions (DL-091). */
const cp = (code: string, isSensitive: boolean): Permission => ({ code, module: code.split('.')[0]!, isSensitive, branchScoped: true });
export const CONSULTATION_PERMISSIONS: Permission[] = [
  cp('consultation.consultants.manage', false),
  cp('consultation.sessions.manage', true),
  cp('consultation.bookings.read', true),
  cp('consultation.bookings.manage', true),
  cp('consultation.notes.read', true),
  cp('support.tickets.read', true),
  cp('support.tickets.manage', true),
];

export const PERMISSIONS: Permission[] = [...M1_PERMISSIONS, ...ACADEMIC_PERMISSIONS, ...FINANCE_PERMISSIONS, ...WORKFLOW_PERMISSIONS, ...CONSULTATION_PERMISSIONS];

const ALL = PERMISSIONS.map((p) => p.code);
const PEOPLE = ALL.filter((c) => /^(students|guardians|teachers|staff)\./.test(c));
const ACADEMIC = ACADEMIC_PERMISSIONS.map((p) => p.code);
/** No sensitive academic data for tenant admins (security baseline #1): catalog.* and classes.* only. */
export const TENANT_ADMIN_PERMISSIONS = ALL.filter(
  (c) => !PEOPLE.includes(c) && (!ACADEMIC.includes(c) || /^(catalog|classes)\./.test(c)) && !c.startsWith('finance.') && (!c.startsWith('workflow.') || c.startsWith('workflow.definitions.'))
    && !c.startsWith('consultation.') && !c.startsWith('support.'),
);
/** Built-in FinanceOfficer (DL-064): every finance code plus the reads finance screens need. */
const FINANCE_OFFICER = [
  ...FINANCE_PERMISSIONS.map((p) => p.code),
  'organization.read', 'branches.read', 'catalog.read', 'classes.read', 'students.read', 'guardians.read', 'enrollments.read',
];
const BRANCH_WORKFLOW = ['workflow.definitions.read', 'workflow.instances.read', 'workflow.instances.manage', 'workflow.tasks.approve'];
/** DL-091: booking oversight and support for branch managers, never consultation.notes.read. */
const BRANCH_CONSULTATION = ['consultation.consultants.manage', 'consultation.bookings.read', 'consultation.bookings.manage', 'support.tickets.read', 'support.tickets.manage'];
const TENANT_CONFIG = TENANT_ADMIN_PERMISSIONS;

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
  roleFinanceOfficer: 'a0000000-0000-4000-8000-000000000007',
  roleConsultant: 'a0000000-0000-4000-8000-000000000008',
  roleSupportDesk: 'a0000000-0000-4000-8000-000000000009',
  userPlatform: 'c0000000-0000-4000-8000-000000000001',
  userDemoAdmin: 'c0000000-0000-4000-8000-000000000002',
  userTeacher: 'c0000000-0000-4000-8000-000000000003',
  userGuardian: 'c0000000-0000-4000-8000-000000000004',
  userStudent: 'c0000000-0000-4000-8000-000000000005',
  userManagerSouth: 'c0000000-0000-4000-8000-000000000006',
  userAlborzAdmin: 'c0000000-0000-4000-8000-000000000007',
  userFinance: 'c0000000-0000-4000-8000-000000000008',
  userFinanceChecker: 'c0000000-0000-4000-8000-000000000009',
  userConsultant: 'c0000000-0000-4000-8000-000000000010',
  student1: 'd0000000-0000-4000-8000-000000000001',
  student2: 'd0000000-0000-4000-8000-000000000002',
  student3: 'd0000000-0000-4000-8000-000000000003',
  guardian1: 'd0000000-0000-4000-8000-000000000011',
  teacher1: 'd0000000-0000-4000-8000-000000000021',
  teacher2: 'd0000000-0000-4000-8000-000000000022',
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
      defaultLocale: 'fa-IR',
      supportedLocales: ['fa-IR', 'en', 'tr', 'ar'],
      timeZone: 'Asia/Tehran',
      currency: 'IRR',
      status: 'active',
      createdAt: created,
      rowVersion: rv(),
    },
    {
      id: IDS.tenantAlborz,
      slug: 'alborz',
      name: 'Alborz Academy',
      defaultLocale: 'fa-IR',
      supportedLocales: ['fa-IR', 'en'],
      timeZone: 'Asia/Tehran',
      currency: 'IRR',
      status: 'active',
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

  const person = <T extends object>(base: T) => ({ status: 'active', userId: null, rowVersion: rv(), ...base });

  const demo: MockTenantData = {
    organization: {
      name: 'Demo Institute Group',
      legalName: 'Demo Education Co.',
      phone: '+982100000000',
      email: 'office@demo.example',
      address: 'Tehran',
      defaultLocale: 'fa-IR',
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
      role(IDS.roleBranchManager, 'BranchManager', 'Branch manager', ['branches.read', ...PEOPLE, ...ACADEMIC, ...BRANCH_WORKFLOW, ...BRANCH_CONSULTATION]),
      role(IDS.roleTeacher, 'Teacher', 'Teacher', ['students.read']),
      role(IDS.roleGuardian, 'Guardian', 'Guardian', []),
      role(IDS.roleStudent, 'Student', 'Student', []),
      role(IDS.roleFinanceOfficer, 'FinanceOfficer', 'Finance officer', FINANCE_OFFICER),
      role(IDS.roleConsultant, 'Consultant', 'Consultant', ['organization.read', 'branches.read', 'consultation.sessions.manage']),
      role(IDS.roleSupportDesk, 'SupportDesk', 'Support desk', ['organization.read', 'branches.read', 'support.tickets.read', 'support.tickets.manage']),
    ],
    students: [
      person({
        id: IDS.student1, branchId: IDS.branchNorth, firstName: 'رضا', lastName: 'احمدی', nationalId: '0012345678',
        birthDate: '2012-04-03', gender: 'male', phone: null, email: null, studentNumber: 'ST-1001',
        userId: IDS.userStudent, guardians: [{ guardianId: IDS.guardian1, relationship: 'father', isPrimary: true }],
      }) as Student,
      person({
        id: IDS.student2, branchId: IDS.branchNorth, firstName: 'Sara', lastName: 'Ahmadi', nationalId: null,
        birthDate: '2014-09-21', gender: 'female', phone: null, email: null, studentNumber: 'ST-1002',
        guardians: [{ guardianId: IDS.guardian1, relationship: 'father', isPrimary: true }],
      }) as Student,
      person({
        id: IDS.student3, branchId: IDS.branchSouth, firstName: 'Mehmet', lastName: 'Yılmaz', nationalId: null,
        birthDate: '2011-01-10', gender: 'male', phone: null, email: null, studentNumber: 'ST-2001', guardians: [],
      }) as Student,
    ],
    guardians: [
      person({
        id: IDS.guardian1, branchId: IDS.branchNorth, firstName: 'علی', lastName: 'احمدی', nationalId: null,
        birthDate: null, gender: 'male', phone: '+989120000000', email: 'guardian@demo.example', userId: IDS.userGuardian,
      }) as Guardian,
    ],
    teachers: [
      person({
        id: IDS.teacher1, branchId: IDS.branchNorth, firstName: 'سارا', lastName: 'کریمی', nationalId: null,
        birthDate: null, gender: 'female', phone: null, email: 'teacher@demo.example', employeeNumber: 'T-01',
        subjects: 'Mathematics, Physics', userId: IDS.userTeacher,
      }) as Teacher,
      person({
        id: IDS.teacher2, branchId: IDS.branchSouth, firstName: 'Hamid', lastName: 'Moradi', nationalId: null,
        birthDate: null, gender: 'male', phone: null, email: null, employeeNumber: 'T-02', subjects: 'Physics',
      }) as Teacher,
    ],
    staff: [
      person({
        id: IDS.staff1, branchId: IDS.branchSouth, firstName: 'Layla', lastName: 'Hassan', nationalId: null,
        birthDate: null, gender: 'female', phone: null, email: null, employeeNumber: 'S-01', position: 'Registrar',
      }) as Staff,
    ],
    academic: createAcademicSeed(rv, IDS),
    finance: emptyFinance(rv),
    workflow: createWorkflowSeed(rv, IDS),
    consultation: createConsultationSeed(rv, IDS),
  };
  demo.finance = createFinanceSeed(rv, IDS, {
    term1: ACADEMIC_IDS.term1,
    enrollmentOf: (studentId) => demo.academic.enrollments.find((e) => e.studentId === studentId && e.classId === ACADEMIC_IDS.classMath7A)?.id,
  });

  const alborz: MockTenantData = {
    organization: {
      name: 'Alborz Academy', legalName: null, phone: null, email: null, address: null,
      defaultLocale: 'fa-IR', timeZone: 'Asia/Tehran', rowVersion: rv(),
    },
    branches: [{ id: IDS.branchAlborz, code: 'A01', name: 'Karaj', address: null, phone: null, isActive: true, rowVersion: rv() }],
    roles: [role(IDS.roleAlborzAdmin, 'TenantAdmin', 'Tenant administrator', TENANT_CONFIG)],
    students: [], guardians: [], teachers: [], staff: [], academic: emptyAcademic(), finance: emptyFinance(rv), workflow: emptyWorkflow(),
    consultation: emptyConsultation(),
  };

  const user = (u: Omit<MockUser, 'rowVersion' | 'email' | 'phone' | 'isActive'> & Partial<MockUser>): MockUser => ({
    email: null, phone: null, isActive: true, rowVersion: rv(), ...u,
  });

  const users: MockUser[] = [
    user({ id: IDS.userPlatform, tenantId: null, userName: 'platform.admin', displayName: 'Platform Administrator', locale: 'en', timeZone: 'UTC', isPlatformAdmin: true, assignments: [] }),
    user({
      id: IDS.userDemoAdmin, tenantId: IDS.tenantDemo, userName: 'demo.admin', displayName: 'مدیر دمو', locale: 'fa-IR', timeZone: 'Asia/Tehran', isPlatformAdmin: false,
      assignments: [{ roleId: IDS.roleTenantAdmin, branchId: null }, { roleId: IDS.roleBranchManager, branchId: IDS.branchNorth }],
    }),
    user({ id: IDS.userManagerSouth, tenantId: IDS.tenantDemo, userName: 'manager.south', displayName: 'South Manager', locale: 'en', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleBranchManager, branchId: IDS.branchSouth }] }),
    user({ id: IDS.userTeacher, tenantId: IDS.tenantDemo, userName: 'teacher.sara', displayName: 'سارا کریمی', locale: 'fa-IR', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleTeacher, branchId: IDS.branchNorth }] }),
    user({ id: IDS.userGuardian, tenantId: IDS.tenantDemo, userName: 'guardian.ali', displayName: 'علی احمدی', locale: 'fa-IR', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleGuardian, branchId: IDS.branchNorth }] }),
    user({ id: IDS.userStudent, tenantId: IDS.tenantDemo, userName: 'student.reza', displayName: 'رضا احمدی', locale: 'fa-IR', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleStudent, branchId: IDS.branchNorth }] }),
    user({ id: IDS.userFinance, tenantId: IDS.tenantDemo, userName: 'finance.officer', displayName: 'Finance Officer', locale: 'en', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleFinanceOfficer, branchId: null }] }),
    user({ id: IDS.userFinanceChecker, tenantId: IDS.tenantDemo, userName: 'finance.checker', displayName: 'Finance Checker', locale: 'en', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleFinanceOfficer, branchId: null }] }),
    user({ id: IDS.userConsultant, tenantId: IDS.tenantDemo, userName: 'consultant.one', displayName: 'نرگس توکلی', locale: 'fa-IR', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleConsultant, branchId: IDS.branchNorth }] }),
    user({ id: IDS.userAlborzAdmin, tenantId: IDS.tenantAlborz, userName: 'alborz.admin', displayName: 'Alborz Admin', locale: 'fa-IR', timeZone: 'Asia/Tehran', isPlatformAdmin: false, assignments: [{ roleId: IDS.roleAlborzAdmin, branchId: null }] }),
  ];

  return { tenants, users, data: { [IDS.tenantDemo]: demo, [IDS.tenantAlborz]: alborz }, version: v };
}
