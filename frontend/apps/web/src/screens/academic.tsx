'use client';

import type { PortalStudent, TeacherClass } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiQuery, useSession } from '@eemp/app';
import {
  AssessmentsPanel, AttendanceSheetPanel, GradebookPanel, PassBadge, personName, PrintButton, ReportCardContentView, ReportCardDownloadButton, ScheduleText,
  ScoreEntryPanel, useFileDownload, usePdfLanguage,
  TimetableView, useAcademicLabels,
} from '@eemp/app/academic';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link, usePathname, useRouter } from '@eemp/i18n/navigation';
import { Badge, Card, DataTable, PageHeader, SelectField, Spinner, StatePanel } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import type { ReactNode } from 'react';

import { usePortalKind } from '../app/[locale]/(portal)/portal-kind';
import { TEACHER_TABS, type TeacherTab } from './academic-tabs';

/* ======================= teacher ======================= */

export function MyClassesScreen() {
  const t = useTranslations('academic.portal');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const query = useApiQuery<TeacherClass[]>('my-classes', (api) => api.academic.portal.myClasses());
  return (
    <>
      <PageHeader title={t('myClassesTitle')} description={t('myClassesIntro')} />
      {query.error && !query.data ? (
        <LoadError error={query.error} onRetry={query.reload} />
      ) : !query.data ? (
        <Spinner label={common('loading')} />
      ) : query.data.length === 0 ? (
        <StatePanel title={t('noClasses')} />
      ) : (
        <div style={{ display: 'grid', gap: 16, gridTemplateColumns: 'repeat(auto-fit, minmax(18rem, 1fr))' }}>
          {query.data.map((c) => (
            <Card key={c.id} aria-label={c.name}>
              <h2 style={{ fontSize: '1.0625rem', marginBlock: 0 }}>
                <Link href={`/classes/${c.id}`}>{c.name}</Link>
              </h2>
              <p style={{ marginBlock: '4px 8px', fontSize: '0.875rem' }}>
                <span className="eemp-ltr">{c.code}</span> · {c.subjectName} · {c.termName}
              </p>
              <p style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBlock: '0 8px' }}>
                <Badge tone="info">{labels.teacherRole(c.role)}</Badge>
                <Badge tone="neutral">{t('students', { count: c.enrolledCount })}</Badge>
              </p>
              <ScheduleText slots={c.schedule} />
            </Card>
          ))}
        </div>
      )}
    </>
  );
}


/**
 * Class workspace for its assigned teacher: attendance, assessments/score entry and gradebook. The API checks the
 * active assignment on every call; approve/publish are never offered to teachers.
 */
export function TeacherClassScreen({ id, tab }: { id: string; tab: TeacherTab }) {
  const t = useTranslations('academic.portal');
  const classesT = useTranslations('academic.classes');
  const common = useTranslations('common');
  const query = useApiQuery(`class|${id}`, (api) => api.academic.classes.get(id));
  const term = useApiQuery(`my-class-term|${id}`, async (api) => (await api.academic.portal.myClasses()).find((c) => c.id === id) ?? null);
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const cls = query.data;
  if (!cls) return <Spinner label={common('loading')} />;
  return (
    <>
      <PageHeader
        before={<Link href="/classes" style={{ fontSize: '0.875rem' }}><span aria-hidden="true" className="eemp-flip">{'←'}</span> {t('backToClasses')}</Link>}
        title={cls.name}
        description={<><span className="eemp-ltr">{cls.code}</span>{term.data ? ` · ${term.data.subjectName} · ${term.data.termName}` : ''}</>}
      />
      <nav aria-label={classesT('sections')} style={{ display: 'flex', gap: 4, flexWrap: 'wrap', marginBlockEnd: 16, borderBlockEnd: '1px solid var(--eemp-color-border)' }}>
        {TEACHER_TABS.map((x) => (
          <Link
            key={x}
            href={`/classes/${cls.id}?tab=${x}`}
            aria-current={x === tab ? 'page' : undefined}
            style={{ padding: '8px 12px', textDecoration: 'none', fontWeight: x === tab ? 700 : 500, borderBlockEnd: x === tab ? '3px solid var(--eemp-color-primary)' : '3px solid transparent' }}
          >
            {classesT('tab', { tab: x })}
          </Link>
        ))}
      </nav>
      {tab === 'attendance' && <AttendanceSheetPanel classId={cls.id} schedule={cls.schedule} canMark />}
      {tab === 'assessments' && <AssessmentsPanel classId={cls.id} canCreate hrefFor={(a) => `/classes/${cls.id}/assessments/${a.id}`} />}
      {tab === 'gradebook' && <GradebookPanel classId={cls.id} />}
      {tab === 'roster' && <TeacherRoster classId={cls.id} />}
    </>
  );
}

function TeacherRoster({ classId }: { classId: string }) {
  const t = useTranslations('academic.roster');
  const common = useTranslations('common');
  const query = useApiQuery(`roster|${classId}`, (api) => api.academic.classes.roster(classId));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  return (
    <Card>
      <DataTable
        caption={t('title')}
        rows={query.data ?? []}
        loading={query.loading}
        loadingLabel={common('loading')}
        getRowKey={(r) => r.enrollmentId}
        empty={<StatePanel title={t('empty')} />}
        columns={[
          { key: 'name', header: t('student'), isRowHeader: true, cell: (r) => personName(r) },
          { key: 'number', header: t('studentNumber'), cell: (r) => <span className="eemp-ltr">{r.studentNumber}</span> },
        ]}
      />
    </Card>
  );
}

export function TeacherAssessmentScreen({ classId, assessmentId }: { classId: string; assessmentId: string }) {
  const t = useTranslations('academic.portal');
  const router = useRouter();
  const a = useApiQuery(`assessment|${assessmentId}`, (api) => api.academic.assessments.get(assessmentId));
  const back = `/classes/${classId}?tab=assessments`;
  return (
    <>
      <PageHeader
        before={<Link href={back} style={{ fontSize: '0.875rem' }}><span aria-hidden="true" className="eemp-flip">{'←'}</span> {t('backToClass')}</Link>}
        title={a.data?.name ?? ''}
      />
      {a.error && !a.data ? (
        <LoadError error={a.error} onRetry={a.reload} />
      ) : (
        <ScoreEntryPanel assessmentId={assessmentId} perms={{ manage: true, approve: false, publish: false }} onDeleted={() => router.push(back)} />
      )}
    </>
  );
}

/* ======================= student / guardian ======================= */

/**
 * The student whose data a page shows: the signed-in student, or — for a guardian — the linked student chosen in
 * the picker (`?student=`). The API returns 404 for anyone else, so the picker is a convenience, not a gate.
 */
function useStudentContext(selected?: string) {
  const kind = usePortalKind();
  const profile = useApiQuery(`portal-profile|${kind}`, (api) => (kind === 'student' ? api.portal.profile() : Promise.resolve(null)));
  const linked = useApiQuery<PortalStudent[]>(`portal-students|${kind}`, (api) => (kind === 'guardian' ? api.portal.students() : Promise.resolve([])));
  if (kind === 'student') {
    return { kind, studentId: profile.data?.profile.id ?? null, students: [] as PortalStudent[], loading: profile.loading, error: profile.error };
  }
  if (kind === 'guardian') {
    const list = linked.data ?? [];
    const studentId = list.find((s) => s.id === selected)?.id ?? list[0]?.id ?? null;
    return { kind, studentId, students: list, loading: linked.loading, error: linked.error };
  }
  return { kind, studentId: null, students: [] as PortalStudent[], loading: false, error: undefined };
}

function StudentPage({
  title, intro, selected, children,
}: { title: string; intro: string; selected?: string; children: (studentId: string) => ReactNode }) {
  const t = useTranslations('academic.portal');
  const common = useTranslations('common');
  const router = useRouter();
  const pathname = usePathname();
  const ctx = useStudentContext(selected);
  return (
    <>
      <PageHeader title={title} description={intro} />
      {ctx.kind === 'guardian' && ctx.students.length > 1 && (
        <div style={{ maxInlineSize: '24rem', marginBlockEnd: 16 }}>
          <SelectField
            label={t('chooseStudent')}
            name="student"
            value={ctx.studentId ?? ''}
            onChange={(e) => router.replace(`${pathname}?student=${encodeURIComponent(e.target.value)}`)}
            options={ctx.students.map((s) => ({ value: s.id, label: `${personName(s)} (${s.studentNumber})` }))}
          />
        </div>
      )}
      {ctx.kind === 'guardian' && ctx.students.length === 1 && (
        <p data-testid="selected-student">{t('showingFor', { name: personName(ctx.students[0]!) })}</p>
      )}
      {ctx.error ? (
        <LoadError error={ctx.error} />
      ) : ctx.loading ? (
        <Spinner label={common('loading')} />
      ) : !ctx.studentId ? (
        <StatePanel title={ctx.kind === 'guardian' ? t('noLinkedStudents') : t('notAStudent')} />
      ) : (
        children(ctx.studentId)
      )}
    </>
  );
}

export function TimetableScreen({ student }: { student?: string }) {
  const t = useTranslations('academic.portal');
  return (
    <StudentPage title={t('timetableTitle')} intro={t('timetableIntro')} selected={student}>
      {(studentId) => <TimetableData key={studentId} studentId={studentId} />}
    </StudentPage>
  );
}

function TimetableData({ studentId }: { studentId: string }) {
  const common = useTranslations('common');
  const query = useApiQuery(`timetable|${studentId}`, (api) => api.academic.portal.timetable(studentId));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  if (!query.data) return <Spinner label={common('loading')} />;
  return <TimetableView entries={query.data} />;
}

export function PortalAttendanceScreen({ student }: { student?: string }) {
  const t = useTranslations('academic.portal');
  return (
    <StudentPage title={t('attendanceTitle')} intro={t('attendanceIntro')} selected={student}>
      {(studentId) => <AttendanceData key={studentId} studentId={studentId} />}
    </StudentPage>
  );
}

function AttendanceData({ studentId }: { studentId: string }) {
  const t = useTranslations('academic.portal');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  const query = useApiQuery(`portal-attendance|${studentId}`, (api) => api.academic.portal.attendance(studentId));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const data = query.data;
  if (!data) return <Spinner label={common('loading')} />;
  return (
    <>
      <p style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }} data-testid="attendance-summary">
        {(['present', 'absent', 'late', 'excused'] as const).map((s) => (
          <Badge key={s} tone={s === 'present' ? 'success' : s === 'absent' ? 'danger' : s === 'late' ? 'warning' : 'info'}>
            {labels.attendance(s)}: {format.number(data.summary[s])}
          </Badge>
        ))}
      </p>
      <DataTable
        caption={t('attendanceTitle')}
        rows={data.records}
        getRowKey={(r) => `${r.date}|${r.classId}|${r.scheduleSlotId ?? ''}`}
        empty={<StatePanel title={t('noAttendance')} />}
        columns={[
          { key: 'date', header: t('date'), isRowHeader: true, cell: (r) => format.calendarDate(r.date) },
          { key: 'class', header: t('class'), cell: (r) => r.className },
          { key: 'status', header: common('status'), cell: (r) => labels.attendance(r.status) },
          { key: 'note', header: t('note'), cell: (r) => r.note || common('none') },
        ]}
      />
    </>
  );
}

export function GradesScreen({ student }: { student?: string }) {
  const t = useTranslations('academic.portal');
  return (
    <StudentPage title={t('gradesTitle')} intro={t('gradesIntro')} selected={student}>
      {(studentId) => <GradesData key={studentId} studentId={studentId} />}
    </StudentPage>
  );
}

function GradesData({ studentId }: { studentId: string }) {
  const t = useTranslations('academic.portal');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  const query = useApiQuery(`portal-grades|${studentId}`, (api) => api.academic.portal.grades(studentId));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  if (!query.data) return <Spinner label={common('loading')} />;
  if (query.data.length === 0) return <StatePanel title={t('noGrades')} />;
  return (
    <div style={{ display: 'grid', gap: 16 }}>
      {query.data.map((c) => (
        <Card key={c.classId} aria-labelledby={`grades-${c.classId}`} data-class={c.className}>
          <h2 id={`grades-${c.classId}`} style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{c.courseName} <span style={{ fontWeight: 400 }}>({c.className})</span></h2>
          <p style={{ marginBlock: '0 8px' }}>
            {t('classTotal')}: <strong data-testid="class-total">{c.total ? labels.total(c.total, c.gradingScheme.decimalPlaces) : common('none')}</strong> <PassBadge total={c.total} />
          </p>
          {c.assessments.length === 0 ? (
            <p>{t('noPublishedYet')}</p>
          ) : (
            <DataTable
              caption={c.className}
              rows={c.assessments}
              getRowKey={(a) => a.id}
              columns={[
                { key: 'name', header: t('assessment'), isRowHeader: true, cell: (a) => a.name },
                { key: 'type', header: t('type'), cell: (a) => labels.assessmentType(a.type) },
                { key: 'weight', header: t('weight'), cell: (a) => labels.number(a.weight) },
                { key: 'score', header: t('score'), cell: (a) => (a.mark === 'scored' ? `${labels.number(a.score)} / ${labels.number(a.maxScore)}` : a.mark ? labels.mark(a.mark) : common('none')) },
                { key: 'published', header: t('publishedAt'), cell: (a) => (a.publishedAt ? format.instantDate(a.publishedAt) : common('none')) },
              ]}
            />
          )}
        </Card>
      ))}
    </div>
  );
}

export function PortalReportCardsScreen({ student }: { student?: string }) {
  const t = useTranslations('academic.portal');
  return (
    <StudentPage title={t('reportCardsTitle')} intro={t('reportCardsIntro')} selected={student}>
      {(studentId) => <ReportCardList key={studentId} studentId={studentId} />}
    </StudentPage>
  );
}

function ReportCardList({ studentId }: { studentId: string }) {
  const t = useTranslations('academic.portal');
  const common = useTranslations('common');
  const format = useLocaleFormat();
  const query = useApiQuery(`portal-report-cards|${studentId}`, (api) => api.academic.portal.reportCards(studentId));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  return (
    <DataTable
      caption={t('reportCardsTitle')}
      rows={query.data ?? []}
      loading={query.loading}
      loadingLabel={common('loading')}
      getRowKey={(r) => r.id}
      empty={<StatePanel title={t('noReportCards')} />}
      columns={[
        { key: 'term', header: t('term'), isRowHeader: true, cell: (r) => <Link href={`/report-cards/${r.id}`}>{r.termName}</Link> },
        { key: 'version', header: t('version'), cell: (r) => format.number(r.versionNo) },
        { key: 'published', header: t('publishedAt'), cell: (r) => (r.publishedAt ? format.instantDate(r.publishedAt) : common('none')) },
      ]}
    />
  );
}

export function PortalReportCardScreen({ id }: { id: string }) {
  const t = useTranslations('academic.portal');
  const common = useTranslations('common');
  const { user } = useSession();
  const query = useApiQuery(`portal-report-card|${id}`, (api) => api.academic.portal.reportCard(id));
  const pdf = useFileDownload();
  const pdfLang = usePdfLanguage();
  return (
    <>
      <PageHeader
        before={<Link href="/report-cards" style={{ fontSize: '0.875rem' }}><span aria-hidden="true" className="eemp-flip">{'←'}</span> {t('backToReportCards')}</Link>}
        title={t('reportCardTitle')}
        actions={
          <>
            {query.data && (
              <ReportCardDownloadButton
                testId="rc-pdf" busy={pdf.busy}
                onClick={() => void pdf.download((api) => api.academic.portal.reportCardPdf(id, pdfLang), 'report-card.pdf')}
              />
            )}
            <PrintButton />
          </>
        }
      />
      <ErrorAlert error={pdf.error} />
      {query.error && !query.data ? (
        <LoadError error={query.error} onRetry={query.reload} />
      ) : !query.data ? (
        <Spinner label={common('loading')} />
      ) : (
        <Card data-user={user.id}>
          <ReportCardContentView content={query.data.content} />
        </Card>
      )}
    </>
  );
}
