import { beforeEach, describe, expect, it } from 'vitest';

import { ACADEMIC_IDS } from '../src/mock/academic';
import { createMockBackend, DEFAULT_MOCK_PASSWORD, MOCK_API_BASE_URL, MOCK_IDS } from '../src/mock';
import type {
  Assessment, AssessmentResults, AttendanceSheet, ClassSection, CurrentUser, Gradebook, GenerateReportCardsResult, PortalClassGrades,
  PortalReportCardSummary, TeacherClass, TokenResponse,
} from '../src';

/** Fixed clock: 2026-11-25 (Wednesday) in Tehran, inside Term 1. */
const NOW = Date.parse('2026-11-25T08:00:00Z');
let backend: ReturnType<typeof createMockBackend>;

const call = async <T = any>(method: string, path: string, token: string, body?: unknown) => { // eslint-disable-line @typescript-eslint/no-explicit-any
  const response = await backend.fetch(`${MOCK_API_BASE_URL}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  return { status: response.status, body: (text ? JSON.parse(text) : undefined) as T };
};

const login = async (userName: string) => {
  const r = await backend.fetch(`${MOCK_API_BASE_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tenantSlug: 'demo', userName, password: DEFAULT_MOCK_PASSWORD }),
  });
  expect(r.status).toBe(200);
  return ((await r.json()) as TokenResponse).accessToken;
};

beforeEach(() => {
  backend = createMockBackend({ now: () => NOW });
});

describe('mock academic: permissions', () => {
  it('tenant admin role holds catalog/classes only; branch manager holds every academic code', async () => {
    const admin = await call<CurrentUser>('GET', '/auth/me', await login('demo.admin'));
    expect(admin.body.permissions).toEqual(expect.arrayContaining(['catalog.manage', 'classes.manage', 'grades.publish']));
    const south = await call<CurrentUser>('GET', '/auth/me', await login('manager.south'));
    expect(south.body.permissions).toContain('reportcards.publish');
    const teacher = await call<CurrentUser>('GET', '/auth/me', await login('teacher.sara'));
    expect(teacher.body.permissions.some((p) => p.startsWith('grades.'))).toBe(false);
  });

  it('teachers act through their assignment, not permissions', async () => {
    const teacher = await login('teacher.sara');
    expect((await call('GET', '/classes', teacher)).status).toBe(403);
    expect((await call<ClassSection>('GET', `/classes/${ACADEMIC_IDS.classMath7A}`, teacher)).status).toBe(200);
    expect((await call('GET', `/classes/${ACADEMIC_IDS.classPhys7S}`, teacher)).status).toBe(404);
    const mine = await call<TeacherClass[]>('GET', '/portal/me/classes', teacher);
    expect(mine.body.map((c) => c.code)).toEqual(['MATH-7A']);
  });
});

describe('mock academic: catalog and classes', () => {
  it('validates terms against their year and overlapping siblings', async () => {
    const admin = await login('demo.admin');
    const base = { academicYearId: ACADEMIC_IDS.year, name: 'Summer' };
    expect((await call('POST', '/terms', admin, { ...base, startDate: '2027-06-01', endDate: '2027-07-30' })).body.code).toBe('academic.term_outside_year');
    expect((await call('POST', '/terms', admin, { ...base, startDate: '2027-01-10', endDate: '2027-02-01' })).body.code).toBe('academic.term_overlap');
    const bad = await call('POST', '/academic-years', admin, { name: 'X', startDate: '2027-01-01', endDate: '2026-01-01' });
    expect(bad.body.errors.endDate).toEqual(['academic.invalid_date_range']);
    expect((await call('DELETE', `/academic-years/${ACADEMIC_IDS.year}`, admin)).body.code).toBe('academic.year_in_use');
  });

  it('reports schedule conflicts and capacity errors', async () => {
    const admin = await login('demo.admin');
    const created = await call<ClassSection>('POST', '/classes', admin, {
      termId: ACADEMIC_IDS.term1, courseId: ACADEMIC_IDS.coursePhys7, branchId: MOCK_IDS.branchNorth, code: 'PHYS-7N', name: 'Physics 7 North', capacity: 1, status: 'active',
    });
    expect(created.status).toBe(201);
    const cls = created.body;
    const room = await call('PUT', `/classes/${cls.id}/schedule`, admin, {
      rowVersion: cls.rowVersion, slots: [{ weekday: 'saturday', startTime: '09:00', endTime: '10:00', room: '101' }],
    });
    expect(room.status).toBe(409);
    expect(room.body.code).toBe('academic.room_conflict');
    const range = await call('PUT', `/classes/${cls.id}/schedule`, admin, {
      rowVersion: cls.rowVersion, slots: [{ weekday: 'saturday', startTime: '11:00', endTime: '10:00' }],
    });
    expect(range.body.errors['slots[0].endTime']).toEqual(['academic.invalid_time_range']);
    const ok = await call<ClassSection>('PUT', `/classes/${cls.id}/schedule`, admin, {
      rowVersion: cls.rowVersion, slots: [{ weekday: 'saturday', startTime: '08:30', endTime: '09:00', room: '102' }],
    });
    expect(ok.status).toBe(200);
    // student1 is in MATH-7A on Saturday 08:00–09:30 → timetable clash
    const clash = await call('POST', '/enrollments', admin, { classId: cls.id, studentId: MOCK_IDS.student1 });
    expect(clash.body.code).toBe('academic.schedule_conflict');
    await call('PUT', `/classes/${cls.id}/schedule`, admin, { rowVersion: ok.body.rowVersion, slots: [] });
    expect((await call('POST', '/enrollments', admin, { classId: cls.id, studentId: MOCK_IDS.student1 })).status).toBe(201);
    const full = await call('POST', '/enrollments', admin, { classId: cls.id, studentId: MOCK_IDS.student2 });
    expect(full.status).toBe(409);
    expect(full.body.code).toBe('academic.class_full');
    const mismatch = await call('POST', '/enrollments', admin, { classId: cls.id, studentId: MOCK_IDS.student3 });
    expect(mismatch.body.code).toBe('academic.student_branch_mismatch');
  });
});

describe('mock academic: attendance', () => {
  it('bulk marks idempotently and rejects future dates and wrong weekdays', async () => {
    const teacher = await login('teacher.sara');
    const url = `/classes/${ACADEMIC_IDS.classMath7A}/attendance`;
    const body = { date: '2026-11-23', scheduleSlotId: ACADEMIC_IDS.slotMathMon, records: [
      { studentId: MOCK_IDS.student1, status: 'present' }, { studentId: MOCK_IDS.student2, status: 'absent', note: 'sick' },
    ] };
    const first = await call<AttendanceSheet>('PUT', url, teacher, body);
    expect(first.status).toBe(200);
    const second = await call<AttendanceSheet>('PUT', url, teacher, body);
    expect(second.body.sessionId).toBe(first.body.sessionId);
    expect(second.body.summary).toMatchObject({ present: 1, absent: 1, unmarked: 0 });
    expect((await call('PUT', url, teacher, { ...body, date: '2026-11-30', scheduleSlotId: null })).body.code).toBe('academic.attendance_future_date');
    expect((await call('PUT', url, teacher, { ...body, date: '2026-11-24' })).body.code).toBe('academic.slot_weekday_mismatch');
    const stranger = await call('PUT', url, teacher, { date: '2026-11-23', records: [{ studentId: MOCK_IDS.student3, status: 'present' }] });
    expect(stranger.body.code).toBe('academic.student_not_enrolled');
  });
});

describe('mock academic: grade workflow and portal visibility', () => {
  it('teacher enters and submits; manager approves and publishes; guardian sees published scores only', async () => {
    const teacher = await login('teacher.sara');
    const manager = await login('demo.admin');
    const guardian = await login('guardian.ali');
    const before = await call<PortalClassGrades[]>('GET', `/portal/me/students/${MOCK_IDS.student1}/grades`, guardian);
    const math = before.body.find((c) => c.classId === ACADEMIC_IDS.classMath7A)!;
    expect(math.assessments.map((a) => a.name)).toEqual(['Quiz 1']);
    expect(math.total?.value).toBe(18.5);

    const results = await call<AssessmentResults>('GET', `/assessments/${ACADEMIC_IDS.midterm}/results`, teacher);
    const incomplete = await call('POST', `/assessments/${ACADEMIC_IDS.midterm}/submit`, teacher, { rowVersion: results.body.assessment.rowVersion });
    expect(incomplete.body.code).toBe('academic.results_incomplete');
    expect(incomplete.body.params.missing).toBe(1);
    const range = await call('PUT', `/assessments/${ACADEMIC_IDS.midterm}/results`, teacher, {
      rowVersion: results.body.assessment.rowVersion, results: [{ studentId: MOCK_IDS.student2, mark: 'scored', score: 25 }],
    });
    expect(range.body.errors['results[0].score']).toEqual(['academic.score_out_of_range']);
    const saved = await call<AssessmentResults>('PUT', `/assessments/${ACADEMIC_IDS.midterm}/results`, teacher, {
      rowVersion: results.body.assessment.rowVersion, results: [{ studentId: MOCK_IDS.student2, mark: 'scored', score: 12 }],
    });
    const submitted = await call<Assessment>('POST', `/assessments/${ACADEMIC_IDS.midterm}/submit`, teacher, { rowVersion: saved.body.assessment.rowVersion });
    expect(submitted.body.status).toBe('submitted');
    // Teachers cannot approve; approving twice is an invalid transition.
    expect((await call('POST', `/assessments/${ACADEMIC_IDS.midterm}/approve`, teacher, { rowVersion: submitted.body.rowVersion })).status).toBe(403);
    const approved = await call<Assessment>('POST', `/assessments/${ACADEMIC_IDS.midterm}/approve`, manager, { rowVersion: submitted.body.rowVersion });
    expect((await call('POST', `/assessments/${ACADEMIC_IDS.midterm}/approve`, manager, { rowVersion: approved.body.rowVersion })).body.code).toBe('academic.invalid_transition');
    const gradebook = await call<Gradebook>('GET', `/classes/${ACADEMIC_IDS.classMath7A}/gradebook`, teacher);
    expect(gradebook.body.students.find((s) => s.studentId === MOCK_IDS.student1)!.total).toEqual({ value: 17.5, label: 'excellent', passed: true });

    // Not yet visible in the portal.
    const still = await call<PortalClassGrades[]>('GET', `/portal/me/students/${MOCK_IDS.student1}/grades`, guardian);
    expect(still.body[0]!.assessments).toHaveLength(1);
    await call('POST', `/assessments/${ACADEMIC_IDS.midterm}/publish`, manager, { rowVersion: approved.body.rowVersion });
    const after = await call<PortalClassGrades[]>('GET', `/portal/me/students/${MOCK_IDS.student1}/grades`, guardian);
    expect(after.body[0]!.assessments.map((a) => a.name).sort()).toEqual(['Midterm', 'Quiz 1']);
    const history = await call<{ action: string }[]>('GET', `/assessments/${ACADEMIC_IDS.midterm}/history`, teacher);
    expect(history.body.map((h) => h.action)).toEqual(['scoreChanged', 'submitted', 'approved', 'published']);
    // Guardians only see linked students.
    expect((await call('GET', `/portal/me/students/${MOCK_IDS.student3}/grades`, guardian)).status).toBe(404);
  });

  it('report cards: generate, publish, regenerate creates a new version', async () => {
    const manager = await login('demo.admin');
    const guardian = await login('guardian.ali');
    const gen = await call<GenerateReportCardsResult>('POST', '/report-cards/generate', manager, { termId: ACADEMIC_IDS.term1 });
    expect(gen.body.created).toBe(2); // North students only (scope)
    expect((await call<PortalReportCardSummary[]>('GET', `/portal/me/students/${MOCK_IDS.student1}/report-cards`, guardian)).body).toEqual([]);
    expect((await call('POST', '/report-cards/publish', manager, { termId: ACADEMIC_IDS.term1 })).body.published).toBe(2);
    const cards = await call<PortalReportCardSummary[]>('GET', `/portal/me/students/${MOCK_IDS.student1}/report-cards`, guardian);
    expect(cards.body).toHaveLength(1);
    const card = await call('GET', `/portal/me/report-cards/${cards.body[0]!.id}`, guardian);
    expect(card.body.content.lines[0].classCode).toBe('MATH-7A');
    expect(card.body.content.attendance.present).toBe(1);
    const again = await call<GenerateReportCardsResult>('POST', '/report-cards/generate', manager, { termId: ACADEMIC_IDS.term1 });
    expect(again.body.unchanged).toBe(2);
  });
});
