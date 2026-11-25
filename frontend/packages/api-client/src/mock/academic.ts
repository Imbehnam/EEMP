/**
 * Mock of the Milestone 2 academic API (development and tests only; NOT a security boundary).
 * Mirrors the shared M2 contract: catalog, classes with schedule/teacher conflicts, enrollments with capacity,
 * idempotent bulk attendance, assessments with the draft → submitted → approved → published workflow and grade
 * history, gradebook totals (weightedAverage.v1), versioned report cards, and the teacher / student / guardian
 * portal views (published data only). Authorization follows the contract: branch-scoped permissions, or the
 * assigned-teacher / guardian / own-student relationship.
 */
import type {
  AcademicYear,
  Assessment,
  AttendanceSheet,
  ClassSection,
  Course,
  GradeHistoryEntry,
  GradeTotal,
  GradingScheme,
  ReportCardContent,
  ReportCardLine,
  ResultMark,
  SchemeInfo,
  Subject,
  TeacherAssignment,
  Term,
} from '../academic-types';
import type { Guid, Student } from '../types';
import { assertValid, HttpError, json, paginate, required, uuid } from './http';
import type { MockTenantData, MockUser } from './seed';

/* ---------------- state ---------------- */

export interface MockAssignment {
  id: Guid;
  teacherId: Guid;
  role: string;
  startDate: string | null;
  endDate: string | null;
}
export interface MockClass extends Omit<ClassSection, 'enrolledCount' | 'teachers' | 'academicYearId' | 'subjectId'> {
  teachers: MockAssignment[];
}
export interface MockEnrollment {
  id: Guid;
  classId: Guid;
  studentId: Guid;
  status: 'active' | 'withdrawn' | 'completed';
  enrolledOn: string;
  withdrawnOn: string | null;
  rowVersion: string;
}
export interface MockSession {
  id: Guid;
  classId: Guid;
  date: string;
  scheduleSlotId: Guid | null;
  takenByUserId: Guid | null;
  updatedAt: string | null;
  rowVersion: string;
  records: { studentId: Guid; status: string; note: string | null }[];
}
interface MockResult {
  studentId: Guid;
  score: number | null;
  mark: string | null;
  comment: string | null;
}
export interface MockAssessment extends Assessment {
  results: MockResult[];
  /** Snapshot copied on publish: the only data the portal sees. */
  published: { weight: number; maxScore: number; results: MockResult[] } | null;
  everPublished: boolean;
}
interface MockReportCard {
  id: Guid;
  studentId: Guid;
  termId: Guid;
  branchId: Guid;
  status: 'draft' | 'published';
  currentVersion: number;
  rowVersion: string;
  versions: { versionNo: number; status: 'draft' | 'published' | 'superseded'; generatedAt: string; publishedAt: string | null; content: ReportCardContent }[];
}

export interface MockAcademic {
  years: AcademicYear[];
  terms: Term[];
  subjects: Subject[];
  courses: Course[];
  schemes: GradingScheme[];
  classes: MockClass[];
  enrollments: MockEnrollment[];
  sessions: MockSession[];
  assessments: MockAssessment[];
  history: (GradeHistoryEntry & { assessmentId: Guid })[];
  reportCards: MockReportCard[];
}

export const emptyAcademic = (): MockAcademic => ({
  years: [], terms: [], subjects: [], courses: [], schemes: [], classes: [], enrollments: [], sessions: [], assessments: [], history: [], reportCards: [],
});

export const ACADEMIC_IDS = {
  year: 'e0000000-0000-4000-8000-000000000001',
  term1: 'e0000000-0000-4000-8000-000000000011',
  term2: 'e0000000-0000-4000-8000-000000000012',
  subjectMath: 'e0000000-0000-4000-8000-000000000021',
  subjectPhys: 'e0000000-0000-4000-8000-000000000022',
  subjectLit: 'e0000000-0000-4000-8000-000000000023',
  courseMath7: 'e0000000-0000-4000-8000-000000000031',
  coursePhys7: 'e0000000-0000-4000-8000-000000000032',
  schemeIr20: 'e0000000-0000-4000-8000-000000000041',
  schemePct: 'e0000000-0000-4000-8000-000000000042',
  schemeLetter: 'e0000000-0000-4000-8000-000000000043',
  classMath7A: 'e0000000-0000-4000-8000-000000000051',
  classPhys7S: 'e0000000-0000-4000-8000-000000000052',
  slotMathSat: 'e0000000-0000-4000-8000-000000000061',
  slotMathMon: 'e0000000-0000-4000-8000-000000000062',
  slotPhysSun: 'e0000000-0000-4000-8000-000000000063',
  quiz1: 'e0000000-0000-4000-8000-000000000071',
  midterm: 'e0000000-0000-4000-8000-000000000072',
} as const;

const scheme = (
  rv: () => string, id: string, code: string, name: string, kind: string, max: number, pass: number, isDefault: boolean, missingCountsAsZero: boolean,
  bands: [string, number][],
): GradingScheme => ({
  id, code, name, kind, minScore: 0, maxScore: max, passScore: pass, decimalPlaces: 2, missingCountsAsZero, isDefault,
  bands: bands.map(([label, minValue]) => ({ label, minValue })), rowVersion: rv(),
});

/** Demo-tenant academic seed (mirrors contract §7 with the mock's people). */
export function createAcademicSeed(
  rv: () => string,
  ids: { branchNorth: string; branchSouth: string; teacher1: string; teacher2: string; student1: string; student2: string; student3: string; userTeacher: string },
): MockAcademic {
  const A = ACADEMIC_IDS;
  const published = '2026-10-21T08:00:00Z';
  const quizResults = [
    { studentId: ids.student1, score: 18.5, mark: 'scored', comment: null },
    { studentId: ids.student2, score: 15, mark: 'scored', comment: null },
  ];
  return {
    years: [{ id: A.year, name: '1405-1406', startDate: '2026-09-23', endDate: '2027-06-21', status: 'active', rowVersion: rv() }],
    terms: [
      { id: A.term1, academicYearId: A.year, name: 'Term 1', startDate: '2026-09-23', endDate: '2027-01-20', sortOrder: 1, rowVersion: rv() },
      { id: A.term2, academicYearId: A.year, name: 'Term 2', startDate: '2027-01-21', endDate: '2027-06-21', sortOrder: 2, rowVersion: rv() },
    ],
    subjects: [
      { id: A.subjectMath, code: 'MATH', name: 'Mathematics', description: null, rowVersion: rv() },
      { id: A.subjectPhys, code: 'PHYS', name: 'Physics', description: null, rowVersion: rv() },
      { id: A.subjectLit, code: 'LIT', name: 'Persian Literature', description: null, rowVersion: rv() },
    ],
    courses: [
      { id: A.courseMath7, subjectId: A.subjectMath, code: 'MATH-7', name: 'Mathematics 7', gradeLevel: '7', description: null, gradingSchemeId: A.schemeIr20, rowVersion: rv() },
      { id: A.coursePhys7, subjectId: A.subjectPhys, code: 'PHYS-7', name: 'Physics 7', gradeLevel: '7', description: null, gradingSchemeId: A.schemeIr20, rowVersion: rv() },
    ],
    schemes: [
      scheme(rv, A.schemeIr20, 'IR20', 'Numeric 0–20', 'numeric', 20, 10, true, true, [['excellent', 17], ['good', 14], ['fair', 12], ['pass', 10], ['fail', 0]]),
      scheme(rv, A.schemePct, 'PCT', 'Percentage 0–100', 'percentage', 100, 50, false, true, []),
      scheme(rv, A.schemeLetter, 'LETTER', 'Letter A–F', 'letter', 100, 60, false, true, [['A', 90], ['B', 80], ['C', 70], ['D', 60], ['F', 0]]),
    ],
    classes: [
      {
        id: A.classMath7A, termId: A.term1, courseId: A.courseMath7, branchId: ids.branchNorth, code: 'MATH-7A', name: 'Mathematics 7 - A',
        capacity: 30, status: 'active', gradingSchemeId: A.schemeIr20, defaultRoom: '101',
        schedule: [
          { id: A.slotMathSat, weekday: 'saturday', startTime: '08:00', endTime: '09:30', room: '101' },
          { id: A.slotMathMon, weekday: 'monday', startTime: '08:00', endTime: '09:30', room: '101' },
        ],
        teachers: [{ id: uuid(), teacherId: ids.teacher1, role: 'primary', startDate: null, endDate: null }],
        rowVersion: rv(),
      },
      {
        id: A.classPhys7S, termId: A.term1, courseId: A.coursePhys7, branchId: ids.branchSouth, code: 'PHYS-7S', name: 'Physics 7 - South',
        capacity: 25, status: 'active', gradingSchemeId: A.schemeIr20, defaultRoom: '201',
        schedule: [{ id: A.slotPhysSun, weekday: 'sunday', startTime: '10:00', endTime: '11:30', room: '201' }],
        teachers: [{ id: uuid(), teacherId: ids.teacher2, role: 'primary', startDate: null, endDate: null }],
        rowVersion: rv(),
      },
    ],
    enrollments: [
      { id: uuid(), classId: A.classMath7A, studentId: ids.student1, status: 'active', enrolledOn: '2026-09-23', withdrawnOn: null, rowVersion: rv() },
      { id: uuid(), classId: A.classMath7A, studentId: ids.student2, status: 'active', enrolledOn: '2026-09-23', withdrawnOn: null, rowVersion: rv() },
      { id: uuid(), classId: A.classPhys7S, studentId: ids.student3, status: 'active', enrolledOn: '2026-09-23', withdrawnOn: null, rowVersion: rv() },
    ],
    sessions: [
      {
        id: uuid(), classId: A.classMath7A, date: '2026-11-21', scheduleSlotId: A.slotMathSat, takenByUserId: ids.userTeacher,
        updatedAt: '2026-11-21T05:00:00Z', rowVersion: rv(),
        records: [
          { studentId: ids.student1, status: 'present', note: null },
          { studentId: ids.student2, status: 'late', note: '10 min' },
        ],
      },
    ],
    assessments: [
      {
        id: A.quiz1, classId: A.classMath7A, name: 'Quiz 1', type: 'quiz', weight: 1, maxScore: 20, date: '2026-10-20', status: 'published',
        submittedAt: published, approvedAt: published, publishedAt: published, returnReason: null, rowVersion: rv(),
        results: quizResults.map((r) => ({ ...r })), published: { weight: 1, maxScore: 20, results: quizResults.map((r) => ({ ...r })) }, everPublished: true,
      },
      {
        id: A.midterm, classId: A.classMath7A, name: 'Midterm', type: 'midterm', weight: 2, maxScore: 20, date: '2026-11-22', status: 'draft',
        submittedAt: null, approvedAt: null, publishedAt: null, returnReason: null, rowVersion: rv(),
        results: [{ studentId: ids.student1, score: 17, mark: 'scored', comment: null }], published: null, everPublished: false,
      },
    ],
    history: [],
    reportCards: [],
  };
}

/* ---------------- helpers ---------------- */

export interface AcademicContext {
  user: MockUser;
  data: MockTenantData;
  /** Branches where the user holds a permission: null = every branch, empty = none. */
  scope: (permission: string) => Set<string> | null;
  now: () => number;
  nextVersion: () => string;
}

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;
const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;
const WEEKDAYS = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday'];
const SAT_FIRST = (d: string) => (WEEKDAYS.indexOf(d) + 1) % 7;

const isValidDate = (s: unknown): s is string => {
  if (typeof s !== 'string' || !ISO_DATE.test(s)) return false;
  const d = new Date(`${s}T00:00:00Z`);
  return !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === s;
};
const weekdayOf = (iso: string) => WEEKDAYS[new Date(`${iso}T00:00:00Z`).getUTCDay()]!;
const includes = (scope: Set<string> | null, branchId: string) => scope === null || scope.has(branchId);
const minutes = (t: string) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3, 5));
const overlaps = (a: { weekday: string; startTime: string; endTime: string }, b: { weekday: string; startTime: string; endTime: string }) =>
  a.weekday === b.weekday && minutes(a.startTime) < minutes(b.endTime) && minutes(b.startTime) < minutes(a.endTime);
const str = (v: unknown) => (typeof v === 'string' ? v.trim() : '');
const optStr = (v: unknown) => (typeof v === 'string' && v.trim() !== '' ? v.trim() : null);
const num = (v: unknown) => (typeof v === 'number' && Number.isFinite(v) ? v : null);
const round = (value: number, places: number) => {
  const f = 10 ** places;
  return (Math.sign(value) * Math.round(Math.abs(value) * f + Number.EPSILON)) / f;
};

function todayIn(timeZone: string, now: number) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date(now));
  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
}

function checkVersion(current: string, body: Record<string, unknown>) {
  if (typeof body.rowVersion !== 'string' || body.rowVersion === '') {
    throw new HttpError(400, 'validation.failed', { errors: { rowVersion: ['validation.required'] } });
  }
  if (body.rowVersion !== current) throw new HttpError(409, 'concurrency.conflict');
}

const duplicate = (field: string) => new HttpError(409, 'resource.duplicate', { params: { field } });
const notFound = () => new HttpError(404, 'resource.not_found');

/* ---------------- routes ---------------- */

/** Handles an academic route, or returns null when the path is not part of the M2 API. */
export function academicRoutes(ctx: AcademicContext, seg: string[], method: string, url: URL, body: Record<string, unknown>): Response | null {
  const { user, data } = ctx;
  const ac = data.academic;
  const nv = ctx.nextVersion;
  const nowIso = () => new Date(ctx.now()).toISOString();
  const today = () => todayIn(data.organization.timeZone || 'Asia/Tehran', ctx.now());

  const demand = (permission: string) => {
    const s = ctx.scope(permission);
    if (s !== null && s.size === 0) throw new HttpError(403, 'authorization.forbidden');
    return s;
  };
  const demandIn = (permission: string, branchId: string) => {
    const s = ctx.scope(permission);
    if (s !== null && s.size === 0) throw new HttpError(403, 'authorization.forbidden');
    if (!includes(s, branchId)) throw new HttpError(403, 'authorization.forbidden_scope');
  };

  const teacherProfile = () => data.teachers.find((t) => t.userId === user.id);
  const studentProfile = () => data.students.find((s) => s.userId === user.id);
  const guardianProfile = () => data.guardians.find((g) => g.userId === user.id);
  const activeOn = (a: MockAssignment, day: string) => (!a.startDate || a.startDate <= day) && (!a.endDate || a.endDate >= day);
  const isAssignedTeacher = (cls: MockClass) => {
    const teacher = teacherProfile();
    const day = today();
    return !!teacher && cls.teachers.some((a) => a.teacherId === teacher.id && activeOn(a, day));
  };

  /** Contract: permission in the class branch, or (when allowed) the assigned teacher; invisible → 404. */
  const classAccess = (classId: string, permission: string, teacherAllowed: boolean, readPermission: string) => {
    const cls = ac.classes.find((c) => c.id === classId);
    if (!cls) throw notFound();
    if (includes(ctx.scope(permission), cls.branchId)) return cls;
    const teacher = isAssignedTeacher(cls);
    if (teacherAllowed && teacher) return cls;
    if (teacher || includes(ctx.scope(readPermission), cls.branchId) || includes(ctx.scope('classes.read'), cls.branchId)) {
      throw new HttpError(403, 'authorization.forbidden_scope');
    }
    throw notFound();
  };

  const termOf = (cls: MockClass) => ac.terms.find((t) => t.id === cls.termId)!;
  const courseOf = (cls: MockClass) => ac.courses.find((c) => c.id === cls.courseId);
  const subjectOf = (cls: MockClass) => ac.subjects.find((s) => s.id === courseOf(cls)?.subjectId);
  const activeEnrollments = (classId: string) => ac.enrollments.filter((e) => e.classId === classId && e.status === 'active');
  const student = (id: string) => data.students.find((s) => s.id === id);
  const teacher = (id: string) => data.teachers.find((t) => t.id === id);

  const classDto = (cls: MockClass): ClassSection => {
    const day = today();
    return {
      ...cls,
      academicYearId: termOf(cls).academicYearId,
      subjectId: courseOf(cls)?.subjectId ?? '',
      enrolledCount: activeEnrollments(cls.id).length,
      schedule: [...cls.schedule].sort((a, b) => SAT_FIRST(a.weekday) - SAT_FIRST(b.weekday) || a.startTime.localeCompare(b.startTime)),
      teachers: cls.teachers.map<TeacherAssignment>((a) => ({
        id: a.id, teacherId: a.teacherId, firstName: teacher(a.teacherId)?.firstName ?? '', lastName: teacher(a.teacherId)?.lastName ?? '',
        role: a.role, startDate: a.startDate, endDate: a.endDate, isActive: activeOn(a, day),
      })),
    };
  };

  const enrollmentDto = (e: MockEnrollment) => {
    const s = student(e.studentId);
    return { ...e, firstName: s?.firstName ?? '', lastName: s?.lastName ?? '', studentNumber: s?.studentNumber ?? '' };
  };

  const schemeById = (id: string | null | undefined) => ac.schemes.find((s) => s.id === id) ?? ac.schemes.find((s) => s.isDefault)!;
  const schemeInfo = (s: GradingScheme): SchemeInfo => ({
    code: s.code, name: s.name, kind: s.kind, minScore: s.minScore, maxScore: s.maxScore, passScore: s.passScore, decimalPlaces: s.decimalPlaces,
  });

  /** weightedAverage.v1 normalized (0..1) or null when no weight counts. */
  const normalized = (inputs: { weight: number; maxScore: number; score: number | null; mark: string | null }[], missingCountsAsZero: boolean) => {
    let weighted = 0;
    let weights = 0;
    for (const i of inputs) {
      if (i.weight <= 0 || i.maxScore <= 0 || i.mark === 'exempt') continue;
      if (i.mark === 'scored' && i.score !== null) {
        weighted += i.weight * Math.min(1, Math.max(0, i.score / i.maxScore));
        weights += i.weight;
      } else if (missingCountsAsZero) {
        weights += i.weight;
      }
    }
    return weights === 0 ? null : weighted / weights;
  };
  const fromNormalized = (n: number, s: GradingScheme): GradeTotal => {
    const value = round(s.minScore + n * (s.maxScore - s.minScore), s.decimalPlaces);
    const band = [...s.bands].sort((a, b) => b.minValue - a.minValue).find((b) => b.minValue <= value);
    return { value, label: band?.label ?? null, passed: value >= s.passScore };
  };
  const total = (inputs: Parameters<typeof normalized>[0], s: GradingScheme) => {
    const n = normalized(inputs, s.missingCountsAsZero);
    return n === null ? null : fromNormalized(n, s);
  };

  const assessmentDto = (a: MockAssessment): Assessment => {
    const { results: _r, published: _p, everPublished: _e, ...dto } = a;
    return dto;
  };
  const resultsDto = (a: MockAssessment) => {
    const active = activeEnrollments(a.classId).map((e) => e.studentId);
    const ids = [...new Set([...active, ...a.results.map((r) => r.studentId)])];
    return {
      assessment: assessmentDto(a),
      results: ids
        .map((sid) => {
          const s = student(sid);
          const r = a.results.find((x) => x.studentId === sid);
          return {
            studentId: sid, firstName: s?.firstName ?? '', lastName: s?.lastName ?? '', studentNumber: s?.studentNumber ?? '',
            score: r?.score ?? null, mark: r?.mark ?? null, comment: r?.comment ?? null,
          };
        })
        .sort((x, y) => x.lastName.localeCompare(y.lastName) || x.firstName.localeCompare(y.firstName)),
    };
  };

  const sheetDto = (cls: MockClass, date: string, slotId: string | null): AttendanceSheet => {
    const session = ac.sessions.find((s) => s.classId === cls.id && s.date === date && s.scheduleSlotId === slotId);
    const roster = activeEnrollments(cls.id).map((e) => e.studentId);
    const ids = [...new Set([...roster, ...(session?.records.map((r) => r.studentId) ?? [])])];
    const entries = ids
      .map((sid) => {
        const s = student(sid);
        const r = session?.records.find((x) => x.studentId === sid);
        return { studentId: sid, firstName: s?.firstName ?? '', lastName: s?.lastName ?? '', studentNumber: s?.studentNumber ?? '', status: r?.status ?? null, note: r?.note ?? null };
      })
      .sort((x, y) => x.lastName.localeCompare(y.lastName) || x.firstName.localeCompare(y.firstName));
    const count = (st: string) => entries.filter((e) => e.status === st).length;
    return {
      sessionId: session?.id ?? null, classId: cls.id, date, scheduleSlotId: slotId, takenByUserId: session?.takenByUserId ?? null,
      updatedAt: session?.updatedAt ?? null, rowVersion: session?.rowVersion ?? null,
      summary: { present: count('present'), absent: count('absent'), late: count('late'), excused: count('excused'), unmarked: entries.filter((e) => !e.status).length },
      entries,
    };
  };

  const attendanceRules = (cls: MockClass, date: unknown, slotId: unknown) => {
    const errors: Record<string, string[]> = {};
    if (!date) errors.date = ['validation.required'];
    else if (!isValidDate(date)) errors.date = ['validation.invalid_date'];
    assertValid(errors);
    const d = date as string;
    let slot = null as MockClass['schedule'][number] | null;
    if (slotId) {
      slot = cls.schedule.find((s) => s.id === slotId) ?? null;
      if (!slot) throw new HttpError(400, 'validation.failed', { errors: { scheduleSlotId: ['academic.slot_not_found'] } });
    }
    const term = termOf(cls);
    if (d < term.startDate || d > term.endDate) throw new HttpError(422, 'academic.date_outside_term');
    if (slot && slot.weekday !== weekdayOf(d)) throw new HttpError(422, 'academic.slot_weekday_mismatch');
    return { date: d, slotId: slot?.id ?? null };
  };

  const history = (a: MockAssessment, entry: Omit<GradeHistoryEntry, 'actorUserId' | 'occurredAt'>) =>
    ac.history.push({ ...entry, assessmentId: a.id, actorUserId: user.id, occurredAt: nowIso() });

  /** Schedule conflicts for a class against other classes of the same term (room / teacher / student). */
  const scheduleConflicts = (cls: MockClass, slots: MockClass['schedule'], teacherIds: string[]) => {
    const day = today();
    const others = ac.classes.filter((c) => c.id !== cls.id && c.termId === cls.termId && c.status !== 'cancelled');
    for (const other of others) {
      const clash = slots.some((a) => other.schedule.some((b) => overlaps(a, b)));
      if (!clash) continue;
      const roomClash = other.branchId === cls.branchId &&
        slots.some((a) => other.schedule.some((b) => overlaps(a, b) && (a.room ?? cls.defaultRoom) && (a.room ?? cls.defaultRoom) === (b.room ?? other.defaultRoom)));
      if (roomClash) throw new HttpError(409, 'academic.room_conflict', { params: { classId: other.id } });
      const t = other.teachers.find((x) => teacherIds.includes(x.teacherId) && activeOn(x, day));
      if (t) throw new HttpError(409, 'academic.teacher_conflict', { params: { classId: other.id, teacherId: t.teacherId } });
      const mine = activeEnrollments(cls.id).map((e) => e.studentId);
      const shared = activeEnrollments(other.id).find((e) => mine.includes(e.studentId));
      if (shared) throw new HttpError(409, 'academic.schedule_conflict', { params: { classId: other.id, studentId: shared.studentId } });
    }
  };

  /* ----- catalog ----- */
  const catalogRead = () => demand('catalog.read');
  const catalogManage = () => demand('catalog.manage');

  if (seg[0] === 'academic-years') {
    if (seg.length === 1 && method === 'GET') {
      catalogRead();
      const items = [...ac.years].sort((a, b) => b.startDate.localeCompare(a.startDate));
      return json(200, paginate(items, url, (y) => [y.name]));
    }
    const save = (existing?: AcademicYear) => {
      catalogManage();
      if (existing) checkVersion(existing.rowVersion, body);
      const errors = required(body, ['name', 'startDate', 'endDate']);
      if (str(body.name).length > 100) errors.name = ['validation.max_length'];
      for (const f of ['startDate', 'endDate']) if (body[f] && !isValidDate(body[f])) errors[f] = ['validation.invalid_date'];
      if (!errors.startDate && !errors.endDate && String(body.startDate) >= String(body.endDate)) errors.endDate = ['academic.invalid_date_range'];
      if (body.status && !['planned', 'active', 'closed'].includes(String(body.status))) errors.status = ['validation.invalid_value'];
      assertValid(errors);
      if (ac.years.some((y) => y.name === str(body.name) && y.id !== existing?.id)) throw duplicate('name');
      const y = existing ?? ({ id: uuid() } as AcademicYear);
      Object.assign(y, {
        name: str(body.name), startDate: body.startDate, endDate: body.endDate,
        status: (body.status as string) || existing?.status || 'planned', rowVersion: nv(),
      });
      if (!existing) ac.years.push(y);
      return y;
    };
    if (seg.length === 1 && method === 'POST') return json(201, save());
    const year = ac.years.find((y) => y.id === seg[1]);
    if (!year) throw notFound();
    if (method === 'GET') {
      catalogRead();
      return json(200, year);
    }
    if (method === 'PUT') return json(200, save(year));
    if (method === 'DELETE') {
      catalogManage();
      if (ac.terms.some((t) => t.academicYearId === year.id)) throw new HttpError(409, 'academic.year_in_use');
      ac.years = ac.years.filter((y) => y !== year);
      return json(204, null);
    }
  }

  if (seg[0] === 'terms') {
    if (seg.length === 1 && method === 'GET') {
      catalogRead();
      const yearId = url.searchParams.get('academicYearId');
      return json(200, ac.terms
        .filter((t) => !yearId || t.academicYearId === yearId)
        .sort((a, b) => a.startDate.localeCompare(b.startDate) || a.sortOrder - b.sortOrder));
    }
    const save = (existing?: Term) => {
      catalogManage();
      if (existing) checkVersion(existing.rowVersion, body);
      const errors = required(body, existing ? ['name', 'startDate', 'endDate'] : ['academicYearId', 'name', 'startDate', 'endDate']);
      for (const f of ['startDate', 'endDate']) if (body[f] && !isValidDate(body[f])) errors[f] = ['validation.invalid_date'];
      if (!errors.startDate && !errors.endDate && String(body.startDate) >= String(body.endDate)) errors.endDate = ['academic.invalid_date_range'];
      const yearId = existing?.academicYearId ?? String(body.academicYearId ?? '');
      const year = ac.years.find((y) => y.id === yearId);
      if (!errors.academicYearId && !year) errors.academicYearId = ['academic.year_not_found'];
      assertValid(errors);
      const start = String(body.startDate);
      const end = String(body.endDate);
      if (start < year!.startDate || end > year!.endDate) throw new HttpError(422, 'academic.term_outside_year');
      const siblings = ac.terms.filter((t) => t.academicYearId === yearId && t.id !== existing?.id);
      if (siblings.some((t) => t.name === str(body.name))) throw duplicate('name');
      if (siblings.some((t) => start <= t.endDate && t.startDate <= end)) throw new HttpError(409, 'academic.term_overlap');
      const t = existing ?? ({ id: uuid(), academicYearId: yearId } as Term);
      Object.assign(t, { name: str(body.name), startDate: start, endDate: end, sortOrder: num(body.sortOrder) ?? existing?.sortOrder ?? siblings.length + 1, rowVersion: nv() });
      if (!existing) ac.terms.push(t);
      return t;
    };
    if (seg.length === 1 && method === 'POST') return json(201, save());
    const term = ac.terms.find((t) => t.id === seg[1]);
    if (!term) throw notFound();
    if (method === 'GET') {
      catalogRead();
      return json(200, term);
    }
    if (method === 'PUT') return json(200, save(term));
    if (method === 'DELETE') {
      catalogManage();
      if (ac.classes.some((c) => c.termId === term.id)) throw new HttpError(409, 'academic.term_in_use');
      ac.terms = ac.terms.filter((t) => t !== term);
      return json(204, null);
    }
  }

  if (seg[0] === 'subjects') {
    if (seg.length === 1 && method === 'GET') {
      catalogRead();
      return json(200, paginate([...ac.subjects].sort((a, b) => a.code.localeCompare(b.code)), url, (s) => [s.code, s.name]));
    }
    const save = (existing?: Subject) => {
      catalogManage();
      if (existing) checkVersion(existing.rowVersion, body);
      const errors = required(body, ['code', 'name']);
      if (str(body.code).length > 30) errors.code = ['validation.max_length'];
      assertValid(errors);
      if (ac.subjects.some((s) => s.code === str(body.code) && s.id !== existing?.id)) throw duplicate('code');
      const s = existing ?? ({ id: uuid() } as Subject);
      Object.assign(s, { code: str(body.code), name: str(body.name), description: optStr(body.description), rowVersion: nv() });
      if (!existing) ac.subjects.push(s);
      return s;
    };
    if (seg.length === 1 && method === 'POST') return json(201, save());
    const subject = ac.subjects.find((s) => s.id === seg[1]);
    if (!subject) throw notFound();
    if (method === 'GET') {
      catalogRead();
      return json(200, subject);
    }
    if (method === 'PUT') return json(200, save(subject));
    if (method === 'DELETE') {
      catalogManage();
      if (ac.courses.some((c) => c.subjectId === subject.id)) throw new HttpError(409, 'academic.subject_in_use');
      ac.subjects = ac.subjects.filter((s) => s !== subject);
      return json(204, null);
    }
  }

  if (seg[0] === 'courses') {
    if (seg.length === 1 && method === 'GET') {
      catalogRead();
      const subjectId = url.searchParams.get('subjectId');
      return json(200, paginate(ac.courses.filter((c) => !subjectId || c.subjectId === subjectId).sort((a, b) => a.code.localeCompare(b.code)), url, (c) => [c.code, c.name]));
    }
    const save = (existing?: Course) => {
      catalogManage();
      if (existing) checkVersion(existing.rowVersion, body);
      const errors = required(body, ['subjectId', 'code', 'name']);
      if (body.subjectId && !ac.subjects.some((s) => s.id === body.subjectId)) errors.subjectId = ['academic.subject_not_found'];
      if (body.gradingSchemeId && !ac.schemes.some((s) => s.id === body.gradingSchemeId)) errors.gradingSchemeId = ['academic.grading_scheme_not_found'];
      assertValid(errors);
      if (ac.courses.some((c) => c.code === str(body.code) && c.id !== existing?.id)) throw duplicate('code');
      const c = existing ?? ({ id: uuid() } as Course);
      Object.assign(c, {
        subjectId: body.subjectId, code: str(body.code), name: str(body.name), gradeLevel: optStr(body.gradeLevel),
        description: optStr(body.description), gradingSchemeId: (body.gradingSchemeId as string) || null, rowVersion: nv(),
      });
      if (!existing) ac.courses.push(c);
      return c;
    };
    if (seg.length === 1 && method === 'POST') return json(201, save());
    const course = ac.courses.find((c) => c.id === seg[1]);
    if (!course) throw notFound();
    if (method === 'GET') {
      catalogRead();
      return json(200, course);
    }
    if (method === 'PUT') return json(200, save(course));
    if (method === 'DELETE') {
      catalogManage();
      if (ac.classes.some((c) => c.courseId === course.id)) throw new HttpError(409, 'academic.course_in_use');
      ac.courses = ac.courses.filter((c) => c !== course);
      return json(204, null);
    }
  }

  if (seg[0] === 'grading-schemes') {
    if (seg.length === 1 && method === 'GET') {
      catalogRead();
      return json(200, ac.schemes);
    }
    const save = (existing?: GradingScheme) => {
      catalogManage();
      if (existing) checkVersion(existing.rowVersion, body);
      const errors = required(body, ['code', 'name', 'kind', 'minScore', 'maxScore', 'passScore']);
      if (body.kind && !['numeric', 'percentage', 'letter'].includes(String(body.kind))) errors.kind = ['validation.invalid_value'];
      const min = num(body.minScore);
      const max = num(body.maxScore);
      const pass = num(body.passScore);
      if (min !== null && max !== null && (min < 0 || max > 1000 || min >= max)) errors.maxScore = ['validation.invalid_value'];
      if (min !== null && max !== null && pass !== null && (pass < min || pass > max)) errors.passScore = ['validation.invalid_value'];
      const places = num(body.decimalPlaces) ?? 2;
      if (places < 0 || places > 4) errors.decimalPlaces = ['validation.invalid_value'];
      const bands = Array.isArray(body.bands) ? (body.bands as { label: string; minValue: number }[]) : [];
      if (bands.some((b) => !str(b.label) || str(b.label).length > 30 || (min !== null && max !== null && (b.minValue < min || b.minValue > max))) ||
        new Set(bands.map((b) => b.minValue)).size !== bands.length) errors.bands = ['validation.invalid_value'];
      assertValid(errors);
      if (ac.schemes.some((s) => s.code === str(body.code) && s.id !== existing?.id)) throw duplicate('code');
      const s = existing ?? ({ id: uuid() } as GradingScheme);
      const isDefault = body.isDefault === true || (existing?.isDefault ?? false);
      if (isDefault) ac.schemes.forEach((x) => { if (x !== s && x.isDefault) { x.isDefault = false; x.rowVersion = nv(); } });
      Object.assign(s, {
        code: str(body.code), name: str(body.name), kind: body.kind, minScore: min, maxScore: max, passScore: pass, decimalPlaces: places,
        missingCountsAsZero: body.missingCountsAsZero !== false, isDefault,
        bands: bands.map((b) => ({ label: str(b.label), minValue: b.minValue })).sort((a, b) => b.minValue - a.minValue), rowVersion: nv(),
      });
      if (!existing) ac.schemes.push(s);
      return s;
    };
    if (seg.length === 1 && method === 'POST') return json(201, save());
    const s = ac.schemes.find((x) => x.id === seg[1]);
    if (!s) throw notFound();
    if (method === 'GET') {
      catalogRead();
      return json(200, s);
    }
    if (method === 'PUT') return json(200, save(s));
    if (method === 'DELETE') {
      catalogManage();
      if (s.isDefault || ac.courses.some((c) => c.gradingSchemeId === s.id) || ac.classes.some((c) => c.gradingSchemeId === s.id)) {
        throw new HttpError(409, 'academic.grading_scheme_in_use');
      }
      ac.schemes = ac.schemes.filter((x) => x !== s);
      return json(204, null);
    }
  }

  /* ----- classes ----- */
  if (seg[0] === 'classes') {
    if (seg.length === 1 && method === 'GET') {
      const scope = demand('classes.read');
      const p = url.searchParams;
      const items = ac.classes
        .filter((c) => includes(scope, c.branchId))
        .filter((c) => (!p.get('termId') || c.termId === p.get('termId')) && (!p.get('branchId') || c.branchId === p.get('branchId')) &&
          (!p.get('courseId') || c.courseId === p.get('courseId')) && (!p.get('status') || c.status === p.get('status')) &&
          (!p.get('teacherId') || c.teachers.some((t) => t.teacherId === p.get('teacherId'))))
        .sort((a, b) => a.code.localeCompare(b.code))
        .map(classDto);
      return json(200, paginate(items, url, (c) => [c.code, c.name]));
    }
    if (seg.length === 1 && method === 'POST') {
      const errors = required(body, ['termId', 'courseId', 'branchId', 'code', 'name', 'capacity']);
      if (body.capacity !== undefined && body.capacity !== null && (!Number.isInteger(body.capacity) || (body.capacity as number) < 1 || (body.capacity as number) > 1000)) {
        errors.capacity = ['validation.invalid_value'];
      }
      if (body.branchId && !data.branches.some((b) => b.id === body.branchId)) errors.branchId = ['academic.branch_not_found'];
      if (body.termId && !ac.terms.some((t) => t.id === body.termId)) errors.termId = ['academic.term_not_found'];
      const course = ac.courses.find((c) => c.id === body.courseId);
      if (body.courseId && !course) errors.courseId = ['academic.course_not_found'];
      if (body.gradingSchemeId && !ac.schemes.some((s) => s.id === body.gradingSchemeId)) errors.gradingSchemeId = ['academic.grading_scheme_not_found'];
      if (body.status && !['planned', 'active', 'completed', 'cancelled'].includes(String(body.status))) errors.status = ['validation.invalid_value'];
      assertValid(errors);
      demandIn('classes.manage', String(body.branchId));
      if (ac.classes.some((c) => c.termId === body.termId && c.code === str(body.code))) throw duplicate('code');
      const cls: MockClass = {
        id: uuid(), termId: String(body.termId), courseId: String(body.courseId), branchId: String(body.branchId), code: str(body.code), name: str(body.name),
        capacity: body.capacity as number, status: (body.status as string) || 'planned',
        gradingSchemeId: (body.gradingSchemeId as string) || course?.gradingSchemeId || schemeById(null).id, defaultRoom: optStr(body.defaultRoom),
        schedule: [], teachers: [], rowVersion: nv(),
      };
      ac.classes.push(cls);
      return json(201, classDto(cls));
    }
    const classId = seg[1]!;
    if (seg.length === 2) {
      if (method === 'GET') return json(200, classDto(classAccess(classId, 'classes.read', true, 'classes.read')));
      const cls = classAccess(classId, 'classes.manage', false, 'classes.read');
      if (method === 'PUT') {
        checkVersion(cls.rowVersion, body);
        const errors = required(body, ['code', 'name', 'capacity']);
        if (body.capacity !== undefined && body.capacity !== null && (!Number.isInteger(body.capacity) || (body.capacity as number) < 1 || (body.capacity as number) > 1000)) {
          errors.capacity = ['validation.invalid_value'];
        }
        if (body.gradingSchemeId && body.gradingSchemeId !== cls.gradingSchemeId) {
          if (!ac.schemes.some((s) => s.id === body.gradingSchemeId)) errors.gradingSchemeId = ['academic.grading_scheme_not_found'];
          else if (ac.assessments.some((a) => a.classId === cls.id)) errors.gradingSchemeId = ['validation.invalid_value'];
        }
        assertValid(errors);
        if ((body.capacity as number) < activeEnrollments(cls.id).length) throw new HttpError(422, 'academic.capacity_below_enrollment');
        if (ac.classes.some((c) => c.termId === cls.termId && c.code === str(body.code) && c.id !== cls.id)) throw duplicate('code');
        Object.assign(cls, {
          code: str(body.code), name: str(body.name), capacity: body.capacity, status: (body.status as string) || cls.status,
          gradingSchemeId: (body.gradingSchemeId as string) || cls.gradingSchemeId, defaultRoom: optStr(body.defaultRoom), rowVersion: nv(),
        });
        return json(200, classDto(cls));
      }
      if (method === 'DELETE') {
        if (ac.enrollments.some((e) => e.classId === cls.id) || ac.sessions.some((s) => s.classId === cls.id) || ac.assessments.some((a) => a.classId === cls.id)) {
          throw new HttpError(409, 'academic.class_in_use');
        }
        ac.classes = ac.classes.filter((c) => c !== cls);
        return json(204, null);
      }
    }
    const sub = seg[2];
    if (sub === 'schedule' && method === 'PUT') {
      const cls = classAccess(classId, 'classes.manage', false, 'classes.read');
      checkVersion(cls.rowVersion, body);
      const slots = Array.isArray(body.slots) ? (body.slots as Record<string, unknown>[]) : [];
      const errors: Record<string, string[]> = {};
      slots.forEach((s, i) => {
        if (!WEEKDAYS.includes(String(s.weekday))) errors[`slots[${i}].weekday`] = ['validation.invalid_value'];
        for (const f of ['startTime', 'endTime']) {
          if (!s[f]) errors[`slots[${i}].${f}`] = ['validation.required'];
          else if (!TIME.test(String(s[f]))) errors[`slots[${i}].${f}`] = ['validation.invalid_format'];
        }
        if (TIME.test(String(s.startTime)) && TIME.test(String(s.endTime)) && minutes(String(s.startTime)) >= minutes(String(s.endTime))) {
          errors[`slots[${i}].endTime`] = ['academic.invalid_time_range'];
        }
      });
      assertValid(errors);
      const parsed = slots.map((s) => ({ id: uuid(), weekday: String(s.weekday), startTime: String(s.startTime), endTime: String(s.endTime), room: optStr(s.room) }));
      for (let i = 0; i < parsed.length; i += 1) {
        for (let j = i + 1; j < parsed.length; j += 1) {
          if (overlaps(parsed[i]!, parsed[j]!)) throw new HttpError(400, 'validation.failed', { errors: { slots: ['academic.slot_overlap'] } });
        }
      }
      scheduleConflicts(cls, parsed, cls.teachers.map((t) => t.teacherId));
      // Keep ids of unchanged slots so attendance sessions stay attached.
      cls.schedule = parsed.map((p) => cls.schedule.find((o) => o.weekday === p.weekday && o.startTime === p.startTime && o.endTime === p.endTime) ? { ...p, id: cls.schedule.find((o) => o.weekday === p.weekday && o.startTime === p.startTime && o.endTime === p.endTime)!.id } : p);
      cls.rowVersion = nv();
      return json(200, classDto(cls));
    }
    if (sub === 'teachers' && method === 'PUT') {
      const cls = classAccess(classId, 'classes.manage', false, 'classes.read');
      checkVersion(cls.rowVersion, body);
      const list = Array.isArray(body.assignments) ? (body.assignments as Record<string, unknown>[]) : [];
      const errors: Record<string, string[]> = {};
      list.forEach((a, i) => {
        if (!a.teacherId) errors[`assignments[${i}].teacherId`] = ['validation.required'];
        else if (!teacher(String(a.teacherId))) errors[`assignments[${i}].teacherId`] = ['academic.teacher_not_found'];
        if (!['primary', 'assistant', 'substitute'].includes(String(a.role))) errors[`assignments[${i}].role`] = ['validation.invalid_value'];
        for (const f of ['startDate', 'endDate']) if (a[f] && !isValidDate(a[f])) errors[`assignments[${i}].${f}`] = ['validation.invalid_date'];
        if (isValidDate(a.startDate) && isValidDate(a.endDate) && a.startDate > a.endDate) errors[`assignments[${i}].endDate`] = ['academic.invalid_date_range'];
      });
      if (new Set(list.map((a) => a.teacherId)).size !== list.length) errors.assignments = ['validation.invalid_value'];
      assertValid(errors);
      if (list.some((a) => teacher(String(a.teacherId))!.branchId !== cls.branchId)) throw new HttpError(422, 'academic.teacher_branch_mismatch');
      const assignments = list.map((a) => ({
        id: cls.teachers.find((o) => o.teacherId === a.teacherId)?.id ?? uuid(), teacherId: String(a.teacherId), role: String(a.role),
        startDate: (a.startDate as string) || null, endDate: (a.endDate as string) || null,
      }));
      const day = today();
      const day0 = (a: MockAssignment) => activeOn(a, day);
      const newTeachers = assignments.filter(day0).map((a) => a.teacherId);
      // Only teacher conflicts are relevant here (room and students did not change).
      for (const other of ac.classes.filter((c) => c.id !== cls.id && c.termId === cls.termId && c.status !== 'cancelled')) {
        const t = other.teachers.find((x) => newTeachers.includes(x.teacherId) && activeOn(x, day));
        if (t && cls.schedule.some((a) => other.schedule.some((b) => overlaps(a, b)))) {
          throw new HttpError(409, 'academic.teacher_conflict', { params: { classId: other.id, teacherId: t.teacherId } });
        }
      }
      cls.teachers = assignments;
      cls.rowVersion = nv();
      return json(200, classDto(cls));
    }
    if (sub === 'roster' && method === 'GET') {
      const cls = classAccess(classId, 'enrollments.read', true, 'enrollments.read');
      const includeInactive = url.searchParams.get('includeInactive') === 'true';
      return json(200, ac.enrollments
        .filter((e) => e.classId === cls.id && (includeInactive || e.status === 'active'))
        .map((e) => {
          const d = enrollmentDto(e);
          return { enrollmentId: e.id, studentId: e.studentId, firstName: d.firstName, lastName: d.lastName, studentNumber: d.studentNumber, status: e.status, enrolledOn: e.enrolledOn };
        })
        .sort((a, b) => a.lastName.localeCompare(b.lastName) || a.firstName.localeCompare(b.firstName)));
    }
    if (sub === 'attendance' && seg.length === 3 && method === 'GET') {
      const cls = classAccess(classId, 'attendance.read', true, 'attendance.read');
      const { date, slotId } = attendanceRules(cls, url.searchParams.get('date'), url.searchParams.get('scheduleSlotId'));
      return json(200, sheetDto(cls, date, slotId));
    }
    if (sub === 'attendance' && seg.length === 3 && method === 'PUT') {
      const cls = classAccess(classId, 'attendance.manage', true, 'attendance.read');
      const records = Array.isArray(body.records) ? (body.records as Record<string, unknown>[]) : [];
      const errors: Record<string, string[]> = {};
      records.forEach((r, i) => {
        if (!r.studentId) errors[`records[${i}].studentId`] = ['validation.required'];
        if (!['present', 'absent', 'late', 'excused'].includes(String(r.status))) errors[`records[${i}].status`] = ['validation.invalid_value'];
      });
      if (new Set(records.map((r) => r.studentId)).size !== records.length) errors.records = ['validation.invalid_value'];
      assertValid(errors);
      const { date, slotId } = attendanceRules(cls, body.date, body.scheduleSlotId);
      if (date > today()) throw new HttpError(422, 'academic.attendance_future_date');
      const enrolled = activeEnrollments(cls.id).map((e) => e.studentId);
      const stranger = records.find((r) => !enrolled.includes(String(r.studentId)));
      if (stranger) throw new HttpError(422, 'academic.student_not_enrolled', { params: { studentId: stranger.studentId } });
      let session = ac.sessions.find((s) => s.classId === cls.id && s.date === date && s.scheduleSlotId === slotId);
      if (session && typeof body.rowVersion === 'string' && body.rowVersion && body.rowVersion !== session.rowVersion) {
        throw new HttpError(409, 'concurrency.conflict');
      }
      if (!session) {
        session = { id: uuid(), classId: cls.id, date, scheduleSlotId: slotId, takenByUserId: user.id, updatedAt: null, rowVersion: nv(), records: [] };
        ac.sessions.push(session);
      }
      for (const r of records) {
        const existing = session.records.find((x) => x.studentId === r.studentId);
        const next = { studentId: String(r.studentId), status: String(r.status), note: optStr(r.note) };
        if (existing) Object.assign(existing, next);
        else session.records.push(next);
      }
      session.takenByUserId = user.id;
      session.updatedAt = nowIso();
      session.rowVersion = nv();
      return json(200, sheetDto(cls, date, slotId));
    }
    if (sub === 'attendance' && seg[3] === 'sessions' && method === 'GET') {
      const cls = classAccess(classId, 'attendance.read', true, 'attendance.read');
      const from = url.searchParams.get('from');
      const to = url.searchParams.get('to');
      return json(200, ac.sessions
        .filter((s) => s.classId === cls.id && (!from || s.date >= from) && (!to || s.date <= to))
        .sort((a, b) => b.date.localeCompare(a.date))
        .map((s) => ({
          id: s.id, date: s.date, scheduleSlotId: s.scheduleSlotId, rowVersion: s.rowVersion,
          present: s.records.filter((r) => r.status === 'present').length, absent: s.records.filter((r) => r.status === 'absent').length,
          late: s.records.filter((r) => r.status === 'late').length, excused: s.records.filter((r) => r.status === 'excused').length,
        })));
    }
    if (sub === 'assessments' && method === 'GET') {
      const cls = classAccess(classId, 'grades.read', true, 'grades.read');
      return json(200, ac.assessments.filter((a) => a.classId === cls.id).sort((a, b) => (a.date ?? '').localeCompare(b.date ?? '')).map(assessmentDto));
    }
    if (sub === 'assessments' && method === 'POST') {
      const cls = classAccess(classId, 'grades.manage', true, 'grades.read');
      const a = { id: uuid(), classId: cls.id, status: 'draft', submittedAt: null, approvedAt: null, publishedAt: null, returnReason: null, results: [], published: null, everPublished: false } as unknown as MockAssessment;
      saveAssessment(a, true);
      ac.assessments.push(a);
      return json(201, assessmentDto(a));
    }
    if (sub === 'gradebook' && method === 'GET') {
      const cls = classAccess(classId, 'grades.read', true, 'grades.read');
      const s = schemeById(cls.gradingSchemeId);
      const list = ac.assessments.filter((a) => a.classId === cls.id).sort((a, b) => (a.date ?? '').localeCompare(b.date ?? ''));
      const ids = [...new Set([...activeEnrollments(cls.id).map((e) => e.studentId), ...list.flatMap((a) => a.results.map((r) => r.studentId))])];
      return json(200, {
        classId: cls.id, gradingScheme: schemeInfo(s), assessments: list.map(assessmentDto),
        students: ids.map((sid) => {
          const p = student(sid);
          const scores = list.map((a) => {
            const r = a.results.find((x) => x.studentId === sid);
            return { assessmentId: a.id, score: r?.score ?? null, mark: r?.mark ?? null };
          });
          return {
            studentId: sid, firstName: p?.firstName ?? '', lastName: p?.lastName ?? '', studentNumber: p?.studentNumber ?? '', scores,
            total: total(list.map((a, i) => ({ weight: a.weight, maxScore: a.maxScore, score: scores[i]!.score, mark: scores[i]!.mark })), s),
          };
        }).sort((a, b) => a.lastName.localeCompare(b.lastName)),
      });
    }
  }

  function saveAssessment(a: MockAssessment, isNew: boolean) {
    const errors = required(body, ['name', 'type', 'weight', 'maxScore']);
    if (body.type && !['exam', 'quiz', 'assignment', 'project', 'participation', 'midterm', 'final', 'other'].includes(String(body.type))) errors.type = ['validation.invalid_value'];
    const weight = num(body.weight);
    const max = num(body.maxScore);
    if (body.weight !== undefined && (weight === null || weight <= 0 || weight > 100)) errors.weight = ['validation.invalid_value'];
    if (body.maxScore !== undefined && (max === null || max <= 0 || max > 1000)) errors.maxScore = ['validation.invalid_value'];
    if (body.date && !isValidDate(body.date)) errors.date = ['validation.invalid_date'];
    assertValid(errors);
    if (!isNew && a.status !== 'draft') throw new HttpError(409, 'academic.assessment_locked');
    Object.assign(a, { name: str(body.name), type: body.type, weight, maxScore: max, date: (body.date as string) || null, rowVersion: nv() });
  }

  /* ----- enrollments ----- */
  if (seg[0] === 'enrollments') {
    if (seg.length === 1 && method === 'GET') {
      const scope = demand('enrollments.read');
      const p = url.searchParams;
      const items = ac.enrollments
        .filter((e) => {
          const cls = ac.classes.find((c) => c.id === e.classId);
          return !!cls && includes(scope, cls.branchId) && (!p.get('classId') || e.classId === p.get('classId')) &&
            (!p.get('studentId') || e.studentId === p.get('studentId')) && (!p.get('termId') || cls.termId === p.get('termId')) &&
            (!p.get('status') || e.status === p.get('status'));
        })
        .map(enrollmentDto);
      return json(200, paginate(items, url, (e) => [e.firstName, e.lastName, e.studentNumber]));
    }
    if (seg.length === 1 && method === 'POST') {
      const errors = required(body, ['classId', 'studentId']);
      if (body.enrolledOn && !isValidDate(body.enrolledOn)) errors.enrolledOn = ['validation.invalid_date'];
      assertValid(errors);
      const cls = classAccess(String(body.classId), 'enrollments.manage', false, 'enrollments.read');
      const s = student(String(body.studentId));
      if (!s) throw new HttpError(400, 'validation.failed', { errors: { studentId: ['academic.student_not_found'] } });
      if (!['planned', 'active'].includes(cls.status)) throw new HttpError(422, 'academic.class_not_open');
      if (s.branchId !== cls.branchId) throw new HttpError(422, 'academic.student_branch_mismatch');
      const existing = ac.enrollments.find((e) => e.classId === cls.id && e.studentId === s.id);
      if (existing?.status === 'active') throw new HttpError(409, 'academic.already_enrolled');
      if (activeEnrollments(cls.id).length >= cls.capacity) throw new HttpError(409, 'academic.class_full');
      const others = ac.enrollments.filter((e) => e.studentId === s.id && e.status === 'active' && e.classId !== cls.id)
        .map((e) => ac.classes.find((c) => c.id === e.classId)!).filter((c) => c && c.termId === cls.termId);
      const clash = others.find((o) => cls.schedule.some((a) => o.schedule.some((b) => overlaps(a, b))));
      if (clash) throw new HttpError(409, 'academic.schedule_conflict', { params: { classId: clash.id } });
      const enrolledOn = (body.enrolledOn as string) || today();
      let e = existing;
      if (e) Object.assign(e, { status: 'active', enrolledOn, withdrawnOn: null, rowVersion: nv() });
      else {
        e = { id: uuid(), classId: cls.id, studentId: s.id, status: 'active', enrolledOn, withdrawnOn: null, rowVersion: nv() };
        ac.enrollments.push(e);
      }
      cls.rowVersion = nv(); // capacity is guarded by the class version
      return json(201, enrollmentDto(e));
    }
    const e = ac.enrollments.find((x) => x.id === seg[1]);
    if (!e) throw notFound();
    if (seg.length === 2 && method === 'GET') {
      classAccess(e.classId, 'enrollments.read', false, 'enrollments.read');
      return json(200, enrollmentDto(e));
    }
    if (seg[2] === 'withdraw' && method === 'POST') {
      classAccess(e.classId, 'enrollments.manage', false, 'enrollments.read');
      checkVersion(e.rowVersion, body);
      if (e.status !== 'active') throw new HttpError(409, 'academic.invalid_transition', { params: { from: e.status, to: 'withdrawn' } });
      const on = (body.withdrawnOn as string) || today();
      if (!isValidDate(on)) throw new HttpError(400, 'validation.failed', { errors: { withdrawnOn: ['validation.invalid_date'] } });
      if (on < e.enrolledOn) throw new HttpError(400, 'validation.failed', { errors: { withdrawnOn: ['academic.invalid_date_range'] } });
      Object.assign(e, { status: 'withdrawn', withdrawnOn: on, rowVersion: nv() });
      return json(200, enrollmentDto(e));
    }
  }

  if (seg[0] === 'attendance' && seg.length === 1 && method === 'GET') {
    const scope = demand('attendance.read');
    const p = url.searchParams;
    const items = ac.sessions.flatMap((s) => {
      const cls = ac.classes.find((c) => c.id === s.classId);
      if (!cls || !includes(scope, cls.branchId)) return [];
      if ((p.get('classId') && s.classId !== p.get('classId')) || (p.get('from') && s.date < p.get('from')!) || (p.get('to') && s.date > p.get('to')!)) return [];
      return s.records
        .filter((r) => (!p.get('studentId') || r.studentId === p.get('studentId')) && (!p.get('status') || r.status === p.get('status')))
        .map((r) => ({ id: `${s.id}:${r.studentId}`, sessionId: s.id, classId: s.classId, studentId: r.studentId, date: s.date, scheduleSlotId: s.scheduleSlotId, status: r.status, note: r.note }));
    });
    return json(200, paginate(items, url, () => []));
  }

  /* ----- assessments ----- */
  if (seg[0] === 'assessments' && seg[1]) {
    const a = ac.assessments.find((x) => x.id === seg[1]);
    if (!a) throw notFound();
    const action = seg[2];
    if (!action && method === 'GET') {
      classAccess(a.classId, 'grades.read', true, 'grades.read');
      return json(200, assessmentDto(a));
    }
    if (!action && method === 'PUT') {
      classAccess(a.classId, 'grades.manage', true, 'grades.read');
      checkVersion(a.rowVersion, body);
      saveAssessment(a, false);
      return json(200, assessmentDto(a));
    }
    if (!action && method === 'DELETE') {
      classAccess(a.classId, 'grades.manage', true, 'grades.read');
      if (a.status !== 'draft' || a.everPublished) throw new HttpError(409, 'academic.assessment_locked');
      ac.assessments = ac.assessments.filter((x) => x !== a);
      return json(204, null);
    }
    if (action === 'results' && method === 'GET') {
      classAccess(a.classId, 'grades.read', true, 'grades.read');
      return json(200, resultsDto(a));
    }
    if (action === 'results' && method === 'PUT') {
      classAccess(a.classId, 'grades.manage', true, 'grades.read');
      checkVersion(a.rowVersion, body);
      const rows = Array.isArray(body.results) ? (body.results as Record<string, unknown>[]) : [];
      const errors: Record<string, string[]> = {};
      rows.forEach((r, i) => {
        if (!r.studentId) errors[`results[${i}].studentId`] = ['validation.required'];
        if (!r.mark && (r.score === null || r.score === undefined)) errors[`results[${i}].mark`] = ['validation.required'];
        else if (r.mark && !['scored', 'absent', 'exempt', 'missing'].includes(String(r.mark))) errors[`results[${i}].mark`] = ['validation.invalid_value'];
        if ((r.mark ?? 'scored') === 'scored' && (r.score === null || r.score === undefined)) errors[`results[${i}].score`] = ['validation.required'];
        else if ((r.mark ?? 'scored') === 'scored' && (typeof r.score !== 'number' || r.score < 0 || r.score > a.maxScore)) errors[`results[${i}].score`] = ['academic.score_out_of_range'];
      });
      if (new Set(rows.map((r) => r.studentId)).size !== rows.length) errors.results = ['validation.invalid_value'];
      assertValid(errors);
      if (a.status !== 'draft') throw new HttpError(409, 'academic.assessment_locked');
      const enrolled = activeEnrollments(a.classId).map((e) => e.studentId);
      const stranger = rows.find((r) => !enrolled.includes(String(r.studentId)) && !a.results.some((x) => x.studentId === r.studentId));
      if (stranger) throw new HttpError(422, 'academic.student_not_enrolled', { params: { studentId: stranger.studentId } });
      for (const r of rows) {
        const mark = (r.mark as ResultMark | undefined) ?? 'scored';
        const score = mark === 'scored' ? (r.score as number) : null;
        const existing = a.results.find((x) => x.studentId === r.studentId);
        const changed = !existing || existing.score !== score || existing.mark !== mark || existing.comment !== optStr(r.comment);
        if (!changed) continue;
        history(a, {
          action: 'scoreChanged', studentId: String(r.studentId), oldScore: existing?.score ?? null, newScore: score,
          oldMark: existing?.mark ?? null, newMark: mark, reason: optStr(r.reason),
        });
        if (existing) Object.assign(existing, { score, mark, comment: optStr(r.comment) });
        else a.results.push({ studentId: String(r.studentId), score, mark, comment: optStr(r.comment) });
      }
      a.rowVersion = nv();
      return json(200, resultsDto(a));
    }
    if (action === 'history' && method === 'GET') {
      classAccess(a.classId, 'grades.read', true, 'grades.read');
      return json(200, ac.history.filter((h) => h.assessmentId === a.id).map(({ assessmentId: _x, ...h }) => h));
    }
    const transitions: Record<string, { from: string; to: string; permission: string | null; audit: string }> = {
      submit: { from: 'draft', to: 'submitted', permission: null, audit: 'submitted' },
      return: { from: 'submitted', to: 'draft', permission: 'grades.approve', audit: 'returned' },
      approve: { from: 'submitted', to: 'approved', permission: 'grades.approve', audit: 'approved' },
      publish: { from: 'approved', to: 'published', permission: 'grades.publish', audit: 'published' },
      reopen: { from: 'published', to: 'draft', permission: 'grades.approve', audit: 'reopened' },
    };
    const t = action ? transitions[action] : undefined;
    if (t && method === 'POST') {
      if (t.permission) {
        demand(t.permission);
        classAccess(a.classId, t.permission, false, 'grades.read');
      } else {
        classAccess(a.classId, 'grades.manage', true, 'grades.read');
      }
      checkVersion(a.rowVersion, body);
      if (action === 'return' || action === 'reopen') {
        if (!str(body.reason)) throw new HttpError(400, 'validation.failed', { errors: { reason: ['validation.required'] } });
      }
      if (a.status !== t.from) throw new HttpError(409, 'academic.invalid_transition', { params: { from: a.status, to: t.to } });
      if (action === 'submit') {
        const missing = activeEnrollments(a.classId).filter((e) => !a.results.some((r) => r.studentId === e.studentId && r.mark)).length;
        if (missing > 0) throw new HttpError(422, 'academic.results_incomplete', { params: { missing } });
      }
      const at = nowIso();
      a.status = t.to;
      if (action === 'submit') Object.assign(a, { submittedAt: at, returnReason: null });
      if (action === 'return') Object.assign(a, { submittedAt: null, returnReason: str(body.reason) });
      if (action === 'approve') a.approvedAt = at;
      if (action === 'publish') {
        a.publishedAt = at;
        a.everPublished = true;
        a.published = { weight: a.weight, maxScore: a.maxScore, results: a.results.map((r) => ({ ...r })) };
      }
      if (action === 'reopen') Object.assign(a, { submittedAt: null, approvedAt: null, returnReason: str(body.reason) });
      history(a, { action: t.audit, studentId: null, oldScore: null, newScore: null, oldMark: null, newMark: null, reason: optStr(body.reason) });
      a.rowVersion = nv();
      return json(200, assessmentDto(a));
    }
  }

  /* ----- report cards ----- */
  const buildContent = (s: Student, term: Term, classIds: string[], generatedAt: string): ReportCardContent => {
    const tenantScheme = schemeById(null);
    const lines: ReportCardLine[] = classIds.map((cid) => {
      const cls = ac.classes.find((c) => c.id === cid)!;
      const sc = schemeById(cls.gradingSchemeId);
      const counted = ac.assessments.filter((a) => a.classId === cid && (a.status === 'approved' || a.status === 'published'));
      const items = counted.map((a) => {
        const r = a.results.find((x) => x.studentId === s.id);
        return { name: a.name, type: a.type, weight: a.weight, maxScore: a.maxScore, score: r?.score ?? null, mark: r?.mark ?? null };
      });
      return {
        classId: cls.id, classCode: cls.code, className: cls.name, courseName: courseOf(cls)?.name ?? '', subjectName: subjectOf(cls)?.name ?? '',
        gradingScheme: schemeInfo(sc), total: total(items, sc), assessments: items,
      };
    }).sort((a, b) => a.classCode.localeCompare(b.classCode));
    const norms = lines.flatMap((l) => {
      if (!l.total) return [];
      const span = l.gradingScheme.maxScore - l.gradingScheme.minScore;
      return span > 0 ? [(l.total.value - l.gradingScheme.minScore) / span] : [];
    });
    const year = ac.years.find((y) => y.id === term.academicYearId)!;
    const sessions = ac.sessions.filter((x) => classIds.includes(x.classId) && x.date >= term.startDate && x.date <= term.endDate);
    const att = (st: string) => sessions.reduce((n, x) => n + x.records.filter((r) => r.studentId === s.id && r.status === st).length, 0);
    return {
      formula: 'weightedAverage.v1', generatedAt,
      student: { id: s.id, firstName: s.firstName, lastName: s.lastName, studentNumber: s.studentNumber },
      academicYear: { id: year.id, name: year.name },
      term: { id: term.id, name: term.name, startDate: term.startDate, endDate: term.endDate },
      gradingScheme: schemeInfo(tenantScheme), lines,
      average: norms.length ? fromNormalized(norms.reduce((x, y) => x + y, 0) / norms.length, tenantScheme) : null,
      attendance: { present: att('present'), absent: att('absent'), late: att('late'), excused: att('excused') },
    };
  };
  const cardSummary = (c: MockReportCard) => {
    const s = student(c.studentId);
    const current = c.versions.find((v) => v.versionNo === c.currentVersion)!;
    const published = [...c.versions].reverse().find((v) => v.status === 'published');
    return {
      id: c.id, studentId: c.studentId, firstName: s?.firstName ?? '', lastName: s?.lastName ?? '', studentNumber: s?.studentNumber ?? '',
      termId: c.termId, branchId: c.branchId, status: c.status, currentVersion: c.currentVersion, publishedVersion: published?.versionNo ?? null,
      generatedAt: current.generatedAt, publishedAt: published?.publishedAt ?? null, average: current.content.average, rowVersion: c.rowVersion,
    };
  };
  const cardDto = (c: MockReportCard) => ({
    ...cardSummary(c),
    versions: c.versions.map(({ content: _c, ...v }) => v),
    content: c.versions.find((v) => v.versionNo === c.currentVersion)!.content,
  });
  const scopeCards = (permission: string) => {
    const errors = required(body, ['termId']);
    assertValid(errors);
    const term = ac.terms.find((t) => t.id === body.termId);
    if (!term) throw new HttpError(400, 'validation.failed', { errors: { termId: ['academic.term_not_found'] } });
    const scope = ctx.scope(permission);
    if (scope !== null && scope.size === 0) throw new HttpError(403, 'authorization.forbidden_scope');
    if (body.branchId && !includes(scope, String(body.branchId))) throw new HttpError(403, 'authorization.forbidden_scope');
    if (body.classId) {
      const cls = classAccess(String(body.classId), permission, false, 'reportcards.read');
      if (cls.termId !== term.id) throw new HttpError(400, 'validation.failed', { errors: { classId: ['validation.invalid_value'] } });
    }
    return { term, scope };
  };

  if (seg[0] === 'report-cards') {
    if (seg[1] === 'generate' && method === 'POST') {
      demand('reportcards.manage');
      const { term, scope } = scopeCards('reportcards.manage');
      const classes = ac.classes.filter((c) => c.termId === term.id && c.status !== 'cancelled');
      const target = classes.filter((c) => includes(scope, c.branchId) && (!body.branchId || c.branchId === body.branchId) && (!body.classId || c.id === body.classId));
      const enrollments = ac.enrollments.filter((e) => classes.some((c) => c.id === e.classId) && e.status !== 'withdrawn');
      const studentIds = [...new Set(enrollments.filter((e) => target.some((c) => c.id === e.classId)).map((e) => e.studentId))];
      const at = nowIso();
      let created = 0;
      let regenerated = 0;
      let unchanged = 0;
      const items: MockReportCard[] = [];
      for (const sid of studentIds) {
        const s = student(sid);
        if (!s || !includes(scope, s.branchId)) continue;
        const content = buildContent(s, term, enrollments.filter((e) => e.studentId === sid).map((e) => e.classId), at);
        let card = ac.reportCards.find((c) => c.studentId === sid && c.termId === term.id);
        if (!card) {
          card = { id: uuid(), studentId: sid, termId: term.id, branchId: s.branchId, status: 'draft', currentVersion: 1, rowVersion: nv(), versions: [{ versionNo: 1, status: 'draft', generatedAt: at, publishedAt: null, content }] };
          ac.reportCards.push(card);
          created += 1;
        } else {
          const current = card.versions.find((v) => v.versionNo === card!.currentVersion)!;
          const same = JSON.stringify({ ...current.content, generatedAt: '' }) === JSON.stringify({ ...content, generatedAt: '' });
          if (same) unchanged += 1;
          else {
            if (current.status === 'draft') Object.assign(current, { content, generatedAt: at });
            else {
              card.currentVersion += 1;
              card.versions.push({ versionNo: card.currentVersion, status: 'draft', generatedAt: at, publishedAt: null, content });
              card.status = 'draft';
            }
            card.rowVersion = nv();
            regenerated += 1;
          }
        }
        items.push(card);
      }
      return json(200, { created, regenerated, unchanged, items: items.map(cardSummary) });
    }
    const publishCard = (c: MockReportCard) => {
      const at = nowIso();
      c.versions.forEach((v) => { if (v.status === 'published') v.status = 'superseded'; });
      const current = c.versions.find((v) => v.versionNo === c.currentVersion)!;
      Object.assign(current, { status: 'published', publishedAt: at });
      c.status = 'published';
      c.rowVersion = nv();
    };
    if (seg[1] === 'publish' && seg.length === 2 && method === 'POST') {
      demand('reportcards.publish');
      const { term, scope } = scopeCards('reportcards.publish');
      const classStudents = body.classId ? ac.enrollments.filter((e) => e.classId === body.classId).map((e) => e.studentId) : null;
      const cards = ac.reportCards.filter((c) => c.termId === term.id && c.status === 'draft' && includes(scope, c.branchId) &&
        (!body.branchId || c.branchId === body.branchId) && (!classStudents || classStudents.includes(c.studentId)));
      cards.forEach(publishCard);
      return json(200, { published: cards.length });
    }
    if (seg.length === 1 && method === 'GET') {
      const scope = demand('reportcards.read');
      const p = url.searchParams;
      const classStudents = p.get('classId') ? ac.enrollments.filter((e) => e.classId === p.get('classId')).map((e) => e.studentId) : null;
      const classTerm = p.get('classId') ? ac.classes.find((c) => c.id === p.get('classId'))?.termId : null;
      const items = ac.reportCards
        .filter((c) => includes(scope, c.branchId) && (!p.get('termId') || c.termId === p.get('termId')) && (!p.get('studentId') || c.studentId === p.get('studentId')) &&
          (!p.get('branchId') || c.branchId === p.get('branchId')) && (!p.get('status') || c.status === p.get('status')) &&
          (!classStudents || (classStudents.includes(c.studentId) && c.termId === classTerm)))
        .map(cardSummary)
        .sort((a, b) => a.lastName.localeCompare(b.lastName));
      return json(200, paginate(items, url, (c) => [c.firstName, c.lastName, c.studentNumber]));
    }
    const card = ac.reportCards.find((c) => c.id === seg[1]);
    const readable = (permission: string) => {
      if (!card) throw notFound();
      const s = ctx.scope(permission);
      if (!includes(s, card.branchId)) {
        if (includes(ctx.scope('reportcards.read'), card.branchId)) throw new HttpError(403, 'authorization.forbidden_scope');
        throw notFound();
      }
      return card;
    };
    if (seg.length === 2 && method === 'GET') {
      demand('reportcards.read');
      return json(200, cardDto(readable('reportcards.read')));
    }
    if (seg[2] === 'versions' && seg[3] && method === 'GET') {
      demand('reportcards.read');
      const c = readable('reportcards.read');
      const v = c.versions.find((x) => x.versionNo === Number(seg[3]));
      if (!v) throw notFound();
      return json(200, v);
    }
    if (seg[2] === 'publish' && method === 'POST') {
      demand('reportcards.publish');
      const c = readable('reportcards.publish');
      checkVersion(c.rowVersion, body);
      if (c.status !== 'draft') throw new HttpError(409, 'academic.invalid_transition', { params: { from: c.status, to: 'published' } });
      publishCard(c);
      return json(200, cardDto(c));
    }
  }

  /* ----- portal ----- */
  if (seg[0] === 'portal' && seg[1] === 'me') {
    if (seg[2] === 'classes' && seg.length === 3 && method === 'GET') {
      const t = teacherProfile();
      if (!t) return json(200, []);
      const termId = url.searchParams.get('termId');
      const day = today();
      return json(200, ac.classes
        .filter((c) => (!termId || c.termId === termId) && c.teachers.some((a) => a.teacherId === t.id && activeOn(a, day)))
        .sort((a, b) => a.code.localeCompare(b.code))
        .map((c) => ({
          id: c.id, code: c.code, name: c.name, courseName: courseOf(c)?.name ?? '', subjectName: subjectOf(c)?.name ?? '', termId: c.termId,
          termName: termOf(c).name, branchId: c.branchId, role: c.teachers.find((a) => a.teacherId === t.id)!.role,
          enrolledCount: activeEnrollments(c.id).length, status: c.status, schedule: classDto(c).schedule,
        })));
    }
    const portalStudent = (studentId: string) => {
      const own = studentProfile();
      const guardian = guardianProfile();
      const s = student(studentId);
      const ok = !!s && (own?.id === studentId || (!!guardian && s.guardians.some((g) => g.guardianId === guardian.id)));
      if (!ok) throw notFound();
      return s!;
    };
    const studentTermClasses = (s: Student, termId: string | null) => {
      const day = today();
      const term = termId ? ac.terms.find((t) => t.id === termId) : ac.terms.find((t) => t.startDate <= day && day <= t.endDate);
      return ac.enrollments
        .filter((e) => e.studentId === s.id && e.status !== 'withdrawn')
        .map((e) => ac.classes.find((c) => c.id === e.classId)!)
        .filter((c) => c && (term ? c.termId === term.id : termId === null));
    };
    if (seg[2] === 'students' && seg[3] && seg[4] === 'timetable' && method === 'GET') {
      const s = portalStudent(seg[3]);
      const classes = studentTermClasses(s, url.searchParams.get('termId'));
      return json(200, classes.flatMap((c) => c.schedule.map((slot) => ({
        classId: c.id, classCode: c.code, className: c.name, courseName: courseOf(c)?.name ?? '', subjectName: subjectOf(c)?.name ?? '',
        weekday: slot.weekday, startTime: slot.startTime, endTime: slot.endTime, room: slot.room ?? c.defaultRoom ?? null,
        teachers: classDto(c).teachers.filter((x) => x.isActive).map((x) => ({ firstName: x.firstName, lastName: x.lastName })),
      }))).sort((a, b) => SAT_FIRST(a.weekday) - SAT_FIRST(b.weekday) || a.startTime.localeCompare(b.startTime)));
    }
    if (seg[2] === 'students' && seg[3] && seg[4] === 'attendance' && method === 'GET') {
      const s = portalStudent(seg[3]);
      const p = url.searchParams;
      const termId = p.get('termId');
      const records = ac.sessions.flatMap((x) => {
        const cls = ac.classes.find((c) => c.id === x.classId);
        const r = x.records.find((y) => y.studentId === s.id);
        if (!cls || !r || (termId && cls.termId !== termId) || (p.get('from') && x.date < p.get('from')!) || (p.get('to') && x.date > p.get('to')!)) return [];
        return [{ date: x.date, classId: cls.id, className: cls.name, scheduleSlotId: x.scheduleSlotId, status: r.status, note: r.note }];
      }).sort((a, b) => b.date.localeCompare(a.date));
      const count = (st: string) => records.filter((r) => r.status === st).length;
      return json(200, { summary: { present: count('present'), absent: count('absent'), late: count('late'), excused: count('excused') }, records });
    }
    if (seg[2] === 'students' && seg[3] && seg[4] === 'grades' && method === 'GET') {
      const s = portalStudent(seg[3]);
      const termId = url.searchParams.get('termId');
      const classes = ac.enrollments
        .filter((e) => e.studentId === s.id && e.status !== 'withdrawn')
        .map((e) => ac.classes.find((c) => c.id === e.classId)!)
        .filter((c) => c && (!termId || c.termId === termId));
      return json(200, classes.map((c) => {
        const sc = schemeById(c.gradingSchemeId);
        const items = ac.assessments.filter((a) => a.classId === c.id && a.published).map((a) => {
          const r = a.published!.results.find((x) => x.studentId === s.id);
          return { id: a.id, name: a.name, type: a.type, weight: a.published!.weight, maxScore: a.published!.maxScore, score: r?.score ?? null, mark: r?.mark ?? null, publishedAt: a.publishedAt ?? null };
        });
        return {
          classId: c.id, className: c.name, courseName: courseOf(c)?.name ?? '', subjectName: subjectOf(c)?.name ?? '', termId: c.termId,
          gradingScheme: schemeInfo(sc), assessments: items, total: total(items, sc),
        };
      }));
    }
    if (seg[2] === 'students' && seg[3] && seg[4] === 'report-cards' && method === 'GET') {
      const s = portalStudent(seg[3]);
      return json(200, ac.reportCards.filter((c) => c.studentId === s.id).flatMap((c) => {
        const v = [...c.versions].reverse().find((x) => x.status === 'published');
        const term = ac.terms.find((t) => t.id === c.termId);
        return v ? [{ id: c.id, termId: c.termId, termName: term?.name ?? '', versionNo: v.versionNo, publishedAt: v.publishedAt }] : [];
      }));
    }
    if (seg[2] === 'report-cards' && seg[3] && method === 'GET') {
      const c = ac.reportCards.find((x) => x.id === seg[3]);
      if (!c) throw notFound();
      portalStudent(c.studentId);
      const v = [...c.versions].reverse().find((x) => x.status === 'published');
      if (!v) throw notFound();
      return json(200, { id: c.id, studentId: c.studentId, termId: c.termId, versionNo: v.versionNo, publishedAt: v.publishedAt, content: v.content });
    }
  }

  return null;
}

