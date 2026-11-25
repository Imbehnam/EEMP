'use client';

import {
  CLASS_STATUSES, TEACHER_ROLES,
  type ClassRequest, type ClassSection, type ClassStatus, type ScheduleSlotInput, type Student, type Teacher, type TeacherRole, type Weekday,
} from '@eemp/api-client';
import { DateField, ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession, toApiErrorLike } from '@eemp/app';
import { AssessmentsPanel, AttendanceSheetPanel, GradebookPanel, personName, ScheduleText, useAcademicLabels } from '@eemp/app/academic';
import { WEEKDAYS } from '@eemp/i18n';
import { useErrorTranslator, useLocaleFormat } from '@eemp/i18n/react';
import { Link, useRouter } from '@eemp/i18n/navigation';
import {
  Alert, Badge, Button, Card, ConfirmDialog, DataTable, FormGrid, PageHeader, SelectField, Spinner, StatePanel, TextField,
} from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useMemo, useState, type FormEvent } from 'react';

import { BackLink, CreateButton, FormActions, Notice, nullIfEmpty, PagedList, useFieldErrors } from '../../components/common';
import { useAcademicLookups } from './options';
import { CLASS_TABS, type ClassTab } from './tabs';

/* ======================= list ======================= */

export function ClassesListScreen({ notice }: { notice?: string }) {
  const t = useTranslations('academic.classes');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const { can } = useSession();
  const lookups = useAcademicLookups();
  const [termId, setTermId] = useState('');
  const [branchId, setBranchId] = useState('');
  const [status, setStatus] = useState('');
  const termLabel = (id: string) => {
    const term = lookups.terms.find((x) => x.id === id);
    const year = term && lookups.years.find((y) => y.id === term.academicYearId);
    return term ? (year ? `${year.name} · ${term.name}` : term.name) : '';
  };
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={can('classes.manage') && <CreateButton href="/academic/classes/new" label={t('new')} />} />
      <Notice notice={notice} />
      <FormGrid style={{ marginBlockEnd: 12 }}>
        <SelectField
          label={t('term')} name="filterTerm" value={termId} onChange={(e) => setTermId(e.target.value)} placeholder={t('allTerms')}
          options={lookups.terms.map((x) => ({ value: x.id, label: termLabel(x.id) }))}
        />
        {lookups.branches.length > 0 && (
          <SelectField
            label={t('branch')} name="filterBranch" value={branchId} onChange={(e) => setBranchId(e.target.value)} placeholder={t('allBranches')}
            options={lookups.branches.map((b) => ({ value: b.id, label: b.name }))}
          />
        )}
        <SelectField
          label={common('status')} name="filterStatus" value={status} onChange={(e) => setStatus(e.target.value)} placeholder={t('allStatuses')}
          options={CLASS_STATUSES.map((s) => ({ value: s, label: labels.classStatus(s) }))}
        />
      </FormGrid>
      <PagedList<ClassSection>
        queryKey={`classes|${termId}|${branchId}|${status}`}
        load={(api, q) => api.academic.classes.list({ ...q, termId: termId || undefined, branchId: branchId || undefined, status: status || undefined })}
        caption={t('title')}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        getRowKey={(c) => c.id}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (c) => <Link href={`/academic/classes/${c.id}`}>{c.name}</Link> },
          { key: 'code', header: t('code'), cell: (c) => <span className="eemp-ltr">{c.code}</span> },
          { key: 'term', header: t('term'), cell: (c) => termLabel(c.termId) || common('none') },
          { key: 'branch', header: t('branch'), cell: (c) => lookups.branches.find((b) => b.id === c.branchId)?.name ?? common('none') },
          { key: 'enrolled', header: t('enrolled'), cell: (c) => t('enrolledValue', { count: labels.number(c.enrolledCount, 0), capacity: labels.number(c.capacity, 0) }) },
          { key: 'teachers', header: t('teachers'), cell: (c) => c.teachers.filter((x) => x.isActive).map(personName).join('، ') || common('none') },
          { key: 'status', header: common('status'), cell: (c) => <Badge tone={c.status === 'active' ? 'success' : 'neutral'}>{labels.classStatus(c.status)}</Badge> },
        ]}
      />
    </>
  );
}

/* ======================= create / edit details ======================= */

type ClassValues = { termId: string; courseId: string; branchId: string; code: string; name: string; capacity: string; status: ClassStatus; gradingSchemeId: string; defaultRoom: string };

const classToRequest = (v: ClassValues): ClassRequest => ({
  termId: v.termId, courseId: v.courseId, branchId: v.branchId, code: v.code.trim(), name: v.name.trim(), capacity: Number(v.capacity),
  status: v.status, gradingSchemeId: v.gradingSchemeId || null, defaultRoom: nullIfEmpty(v.defaultRoom),
});

function ClassFields({ v, set, errors, isEdit, disabled }: { v: ClassValues; set: (v: ClassValues) => void; errors: Record<string, string>; isEdit?: boolean; disabled?: boolean }) {
  const t = useTranslations('academic.classes');
  const labels = useAcademicLabels();
  const lookups = useAcademicLookups();
  return (
    <fieldset disabled={disabled} style={{ border: 0, padding: 0, margin: 0 }}>
      <FormGrid>
        <SelectField
          label={t('term')} name="termId" required disabled={isEdit} value={v.termId} error={errors.termId} placeholder={t('choose')}
          hint={isEdit ? t('immutableHint') : undefined} onChange={(e) => set({ ...v, termId: e.target.value })}
          options={lookups.terms.map((x) => ({ value: x.id, label: `${lookups.years.find((y) => y.id === x.academicYearId)?.name ?? ''} · ${x.name}` }))}
        />
        <SelectField
          label={t('course')} name="courseId" required disabled={isEdit} value={v.courseId} error={errors.courseId} placeholder={t('choose')}
          onChange={(e) => set({ ...v, courseId: e.target.value })}
          options={lookups.courses.map((c) => ({ value: c.id, label: `${c.name} (${c.code})` }))}
        />
        <SelectField
          label={t('branch')} name="branchId" required disabled={isEdit} value={v.branchId} error={errors.branchId} placeholder={t('choose')}
          onChange={(e) => set({ ...v, branchId: e.target.value })}
          options={lookups.branches.map((b) => ({ value: b.id, label: b.name }))}
        />
        <TextField label={t('code')} name="code" dir="ltr" required maxLength={30} value={v.code} error={errors.code} onChange={(e) => set({ ...v, code: e.target.value })} />
        <TextField label={t('name')} name="name" required value={v.name} error={errors.name} onChange={(e) => set({ ...v, name: e.target.value })} />
        <TextField label={t('capacity')} name="capacity" type="number" min={1} max={1000} dir="ltr" required value={v.capacity} error={errors.capacity} onChange={(e) => set({ ...v, capacity: e.target.value })} />
        <SelectField
          label={t('status')} name="status" value={v.status} error={errors.status} onChange={(e) => set({ ...v, status: e.target.value as ClassStatus })}
          options={CLASS_STATUSES.map((s) => ({ value: s, label: labels.classStatus(s) }))}
        />
        <SelectField
          label={t('gradingScheme')} name="gradingSchemeId" value={v.gradingSchemeId} error={errors.gradingSchemeId} placeholder={t('courseDefault')}
          hint={t('gradingSchemeHint')} onChange={(e) => set({ ...v, gradingSchemeId: e.target.value })}
          options={lookups.schemes.map((s) => ({ value: s.id, label: `${s.name} (${s.code})` }))}
        />
        <TextField label={t('defaultRoom')} name="defaultRoom" maxLength={50} value={v.defaultRoom} error={errors.defaultRoom} onChange={(e) => set({ ...v, defaultRoom: e.target.value })} />
      </FormGrid>
    </fieldset>
  );
}

export function ClassCreateScreen() {
  const t = useTranslations('academic.classes');
  const router = useRouter();
  const [v, setV] = useState<ClassValues>({ termId: '', courseId: '', branchId: '', code: '', name: '', capacity: '30', status: 'planned', gradingSchemeId: '', defaultRoom: '' });
  const mutation = useApiMutation((api, values: ClassValues) => api.academic.classes.create(classToRequest(values)));
  const errors = useFieldErrors(mutation.error);
  return (
    <>
      <PageHeader before={<BackLink href="/academic/classes" />} title={t('createTitle')} />
      <Card>
        <ErrorAlert error={mutation.error} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            const result = await mutation.run(v);
            if (result.ok) router.push(`/academic/classes/${result.value.id}?notice=created`);
          }}
        >
          <ClassFields v={v} set={setV} errors={errors} />
          <FormActions cancelHref="/academic/classes" busy={mutation.busy} />
        </form>
      </Card>
    </>
  );
}

/* ======================= detail with tabs ======================= */


export function ClassDetailScreen({ id, tab, notice }: { id: string; tab: ClassTab; notice?: string }) {
  const t = useTranslations('academic.classes');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const { can } = useSession();
  const lookups = useAcademicLookups();
  const query = useApiQuery(`class|${id}`, (api) => api.academic.classes.get(id));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const cls = query.data;
  if (!cls) return <Spinner label={common('loading')} />;
  const term = lookups.terms.find((x) => x.id === cls.termId);
  const tabs = CLASS_TABS.filter((x) =>
    x === 'overview' ? true
      : x === 'roster' ? can('enrollments.read')
        : x === 'attendance' ? can('attendance.read')
          : can('grades.read'),
  );
  const active = tabs.includes(tab) ? tab : 'overview';
  return (
    <>
      <PageHeader
        before={<BackLink href="/academic/classes" />}
        title={cls.name}
        description={<>{<span className="eemp-ltr">{cls.code}</span>} · {labels.classStatus(cls.status)} · {t('enrolledValue', { count: labels.number(cls.enrolledCount, 0), capacity: labels.number(cls.capacity, 0) })}</>}
      />
      <Notice notice={notice} />
      <nav aria-label={t('sections')} style={{ display: 'flex', gap: 4, flexWrap: 'wrap', marginBlockEnd: 16, borderBlockEnd: '1px solid var(--eemp-color-border)' }}>
        {tabs.map((x) => (
          <Link
            key={x}
            href={`/academic/classes/${cls.id}?tab=${x}`}
            aria-current={x === active ? 'page' : undefined}
            style={{
              padding: '8px 12px', textDecoration: 'none', fontWeight: x === active ? 700 : 500,
              borderBlockEnd: x === active ? '3px solid var(--eemp-color-primary)' : '3px solid transparent',
            }}
          >
            {t('tab', { tab: x })}
          </Link>
        ))}
      </nav>
      {active === 'overview' && <ClassOverview cls={cls} onChange={(c) => query.setData(c)} reload={query.reload} />}
      {active === 'roster' && <RosterPanel cls={cls} onChanged={query.reload} />}
      {active === 'attendance' && (
        <AttendanceSheetPanel
          classId={cls.id}
          schedule={cls.schedule}
          canMark={can('attendance.manage')}
          termStart={term?.startDate}
          termEnd={term?.endDate}
        />
      )}
      {active === 'assessments' && (
        <AssessmentsPanel classId={cls.id} canCreate={can('grades.manage')} hrefFor={(a) => `/academic/assessments/${a.id}`} />
      )}
      {active === 'gradebook' && <GradebookPanel classId={cls.id} />}
    </>
  );
}

function ClassOverview({ cls, onChange, reload }: { cls: ClassSection; onChange: (c: ClassSection) => void; reload: () => void }) {
  const t = useTranslations('academic.classes');
  const common = useTranslations('common');
  const router = useRouter();
  const { can } = useSession();
  const canManage = can('classes.manage');
  const [values, setValues] = useState<ClassValues | null>(null);
  const [saved, setSaved] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const mutation = useApiMutation((api, c: ClassSection, v: ClassValues) => api.academic.classes.update(c.id, { ...classToRequest(v), rowVersion: c.rowVersion }));
  const remove = useApiMutation((api, classId: string) => api.academic.classes.remove(classId));
  const errors = useFieldErrors(mutation.error);
  const v = values ?? {
    termId: cls.termId, courseId: cls.courseId, branchId: cls.branchId, code: cls.code, name: cls.name, capacity: String(cls.capacity),
    status: cls.status as ClassStatus, gradingSchemeId: cls.gradingSchemeId, defaultRoom: cls.defaultRoom ?? '',
  };
  return (
    <>
      <Card aria-labelledby="class-details-heading">
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
          <h2 id="class-details-heading" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('detailsTitle')}</h2>
          {canManage && <Button variant="danger" onClick={() => setConfirmDelete(true)}>{common('delete')}</Button>}
        </div>
        {saved && <Alert tone="success" role="status">{common('saved')}</Alert>}
        <ErrorAlert error={mutation.error} onReload={() => { mutation.reset(); setValues(null); reload(); }} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            setSaved(false);
            const result = await mutation.run(cls, v);
            if (result.ok) {
              onChange(result.value);
              setValues(null);
              setSaved(true);
            }
          }}
        >
          <ClassFields v={v} set={setValues} errors={errors} isEdit disabled={!canManage} />
          {canManage && <FormActions cancelHref="/academic/classes" busy={mutation.busy} />}
        </form>
      </Card>
      <ScheduleEditor cls={cls} canManage={canManage} onChange={onChange} reload={reload} />
      <TeachersEditor cls={cls} canManage={canManage} onChange={onChange} reload={reload} />
      <ConfirmDialog
        open={confirmDelete}
        title={common('confirmDeleteTitle', { name: cls.name })}
        description={common('confirmDeleteBody')}
        confirmLabel={common('delete')}
        cancelLabel={common('cancel')}
        busy={remove.busy}
        onCancel={() => { remove.reset(); setConfirmDelete(false); }}
        onConfirm={async () => {
          const result = await remove.run(cls.id);
          if (result.ok) router.push('/academic/classes?notice=deleted');
        }}
      >
        <ErrorAlert error={remove.error} />
      </ConfirmDialog>
    </>
  );
}

/** Indexed field errors (`slots[0].endTime`) → { "0.endTime": message }. */
function useIndexedErrors(error: unknown, collection: string) {
  const errors = useErrorTranslator();
  return useMemo(() => {
    const out: Record<string, string> = {};
    if (!error) return out;
    for (const [key, codes] of Object.entries(toApiErrorLike(error).fieldErrors)) {
      const m = new RegExp(`^${collection}\\[(\\d+)\\]\\.(\\w+)$`).exec(key);
      if (m && codes[0]) out[`${m[1]}.${m[2]}`] = errors.code(codes[0]);
      else if (key === collection && codes[0]) out._ = errors.code(codes[0]);
    }
    return out;
  }, [error, errors, collection]);
}

/** Conflict details (room / teacher / student) named from the problem params. */
function ConflictDetails({ error }: { error: unknown }) {
  const t = useTranslations('academic.schedule');
  const classId = error ? toApiErrorLike(error).params.classId : undefined;
  const lookups = useApiQuery(`conflict-class|${String(classId ?? '')}`, async (api) =>
    typeof classId === 'string' ? api.academic.classes.get(classId).catch(() => null) : null,
  );
  if (!error) return null;
  const code = toApiErrorLike(error).code;
  if (!['academic.room_conflict', 'academic.teacher_conflict', 'academic.schedule_conflict'].includes(code) || !lookups.data) return null;
  return <p style={{ marginBlock: 4 }} data-testid="conflict-class">{t('conflictWith', { name: `${lookups.data.name} (${lookups.data.code})` })}</p>;
}

type SlotRow = { weekday: Weekday; startTime: string; endTime: string; room: string };

function ScheduleEditor({ cls, canManage, onChange, reload }: { cls: ClassSection; canManage: boolean; onChange: (c: ClassSection) => void; reload: () => void }) {
  const t = useTranslations('academic.schedule');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const [rows, setRows] = useState<SlotRow[] | null>(null);
  const [saved, setSaved] = useState(false);
  const mutation = useApiMutation((api, c: ClassSection, slots: ScheduleSlotInput[]) => api.academic.classes.setSchedule(c.id, { slots, rowVersion: c.rowVersion }));
  const errors = useIndexedErrors(mutation.error, 'slots');
  const editing = rows !== null;
  return (
    <Card aria-labelledby="schedule-heading" style={{ marginBlockStart: 16 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', alignItems: 'center' }}>
        <h2 id="schedule-heading" style={{ fontSize: '1.0625rem', margin: 0 }}>{t('title')}</h2>
        {canManage && !editing && (
          <Button variant="secondary" onClick={() => {
            setSaved(false);
            mutation.reset();
            setRows(cls.schedule.map((s) => ({ weekday: s.weekday as Weekday, startTime: s.startTime, endTime: s.endTime, room: s.room ?? '' })));
          }}>
            {t('edit')}
          </Button>
        )}
      </div>
      <p style={{ fontSize: '0.875rem' }}>{t('intro')}</p>
      {saved && <Alert tone="success" role="status">{common('saved')}</Alert>}
      {!editing ? (
        <div data-testid="schedule"><ScheduleText slots={cls.schedule} /></div>
      ) : (
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            const result = await mutation.run(cls, rows.map((r) => ({ weekday: r.weekday, startTime: r.startTime, endTime: r.endTime, room: nullIfEmpty(r.room) })));
            if (result.ok) {
              onChange(result.value);
              setRows(null);
              setSaved(true);
            }
          }}
        >
          <ErrorAlert error={mutation.error} onReload={() => { mutation.reset(); setRows(null); reload(); }} />
          <ConflictDetails error={mutation.error} />
          {errors._ && <p role="alert" style={{ color: 'var(--eemp-color-danger)' }}>{errors._}</p>}
          {rows.map((r, i) => {
            const set = (patch: Partial<SlotRow>) => setRows(rows.map((x, j) => (j === i ? { ...x, ...patch } : x)));
            return (
              <fieldset key={i} style={{ border: 0, padding: 0, margin: '0 0 8px' }}>
                <legend className="eemp-visually-hidden">{t('slotLegend', { n: i + 1 })}</legend>
                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-start' }}>
                  <SelectField
                    label={t('weekday')} value={r.weekday} error={errors[`${i}.weekday`]} onChange={(e) => set({ weekday: e.target.value as Weekday })}
                    options={WEEKDAYS.map((d) => ({ value: d, label: labels.weekday(d) }))}
                  />
                  <TextField label={t('start')} type="time" dir="ltr" required value={r.startTime} error={errors[`${i}.startTime`]} onChange={(e) => set({ startTime: e.target.value })} />
                  <TextField label={t('end')} type="time" dir="ltr" required value={r.endTime} error={errors[`${i}.endTime`]} onChange={(e) => set({ endTime: e.target.value })} />
                  <TextField label={t('room')} value={r.room} maxLength={50} error={errors[`${i}.room`]} onChange={(e) => set({ room: e.target.value })} />
                  <Button type="button" variant="ghost" size="sm" style={{ marginBlockStart: 28 }} onClick={() => setRows(rows.filter((_, j) => j !== i))}>
                    {t('removeSlot')}
                  </Button>
                </div>
              </fieldset>
            );
          })}
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBlockStart: 8 }}>
            <Button type="button" variant="secondary" onClick={() => setRows([...rows, { weekday: 'saturday', startTime: '08:00', endTime: '09:30', room: cls.defaultRoom ?? '' }])}>
              {t('addSlot')}
            </Button>
            <Button type="submit" loading={mutation.busy}>{t('save')}</Button>
            <Button type="button" variant="secondary" onClick={() => setRows(null)}>{common('cancel')}</Button>
          </div>
        </form>
      )}
    </Card>
  );
}

type AssignmentRow = { teacherId: string; role: TeacherRole; startDate: string; endDate: string };

function TeachersEditor({ cls, canManage, onChange, reload }: { cls: ClassSection; canManage: boolean; onChange: (c: ClassSection) => void; reload: () => void }) {
  const t = useTranslations('academic.teachers');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  const { can } = useSession();
  const teachers = useApiQuery<Teacher[]>(`branch-teachers|${cls.branchId}|${can('teachers.read')}`, async (api) =>
    can('teachers.read') ? (await api.people('teachers').list({ pageSize: 100 })).items.filter((x) => x.branchId === cls.branchId) : [],
  );
  const [rows, setRows] = useState<AssignmentRow[] | null>(null);
  const [saved, setSaved] = useState(false);
  const mutation = useApiMutation((api, c: ClassSection, list: AssignmentRow[]) =>
    api.academic.classes.setTeachers(c.id, {
      rowVersion: c.rowVersion,
      assignments: list.map((r) => ({ teacherId: r.teacherId, role: r.role, startDate: r.startDate || null, endDate: r.endDate || null })),
    }),
  );
  const errors = useIndexedErrors(mutation.error, 'assignments');
  const options = (teachers.data ?? []).map((x) => ({ value: x.id, label: `${personName(x)} (${x.employeeNumber})` }));
  // Keep currently assigned teachers selectable even when the people list is not readable.
  for (const a of cls.teachers) if (!options.some((o) => o.value === a.teacherId)) options.push({ value: a.teacherId, label: personName(a) });
  return (
    <Card aria-labelledby="teachers-heading" style={{ marginBlockStart: 16 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', alignItems: 'center' }}>
        <h2 id="teachers-heading" style={{ fontSize: '1.0625rem', margin: 0 }}>{t('title')}</h2>
        {canManage && rows === null && (
          <Button variant="secondary" onClick={() => {
            setSaved(false);
            mutation.reset();
            setRows(cls.teachers.map((a) => ({ teacherId: a.teacherId, role: a.role as TeacherRole, startDate: a.startDate ?? '', endDate: a.endDate ?? '' })));
          }}>
            {t('edit')}
          </Button>
        )}
      </div>
      {saved && <Alert tone="success" role="status">{common('saved')}</Alert>}
      {rows === null ? (
        cls.teachers.length === 0 ? <p>{t('empty')}</p> : (
          <ul style={{ paddingInlineStart: 18 }} data-testid="teachers">
            {cls.teachers.map((a) => (
              <li key={a.id}>
                {personName(a)} · {labels.teacherRole(a.role)}
                {(a.startDate || a.endDate) && ` · ${a.startDate ? format.calendarDate(a.startDate) : '…'} – ${a.endDate ? format.calendarDate(a.endDate) : '…'}`}
                {!a.isActive && <> <Badge tone="neutral">{t('inactive')}</Badge></>}
              </li>
            ))}
          </ul>
        )
      ) : (
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            const result = await mutation.run(cls, rows);
            if (result.ok) {
              onChange(result.value);
              setRows(null);
              setSaved(true);
            }
          }}
        >
          <ErrorAlert error={mutation.error} onReload={() => { mutation.reset(); setRows(null); reload(); }} />
          <ConflictDetails error={mutation.error} />
          {errors._ && <p role="alert" style={{ color: 'var(--eemp-color-danger)' }}>{errors._}</p>}
          {rows.map((r, i) => {
            const set = (patch: Partial<AssignmentRow>) => setRows(rows.map((x, j) => (j === i ? { ...x, ...patch } : x)));
            return (
              <fieldset key={i} style={{ border: 0, padding: 0, margin: '0 0 8px' }}>
                <legend className="eemp-visually-hidden">{t('rowLegend', { n: i + 1 })}</legend>
                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-start' }}>
                  <SelectField label={t('teacher')} required value={r.teacherId} placeholder={t('choose')} error={errors[`${i}.teacherId`]} onChange={(e) => set({ teacherId: e.target.value })} options={options} />
                  <SelectField
                    label={t('role')} value={r.role} error={errors[`${i}.role`]} onChange={(e) => set({ role: e.target.value as TeacherRole })}
                    options={TEACHER_ROLES.map((x) => ({ value: x, label: labels.teacherRole(x) }))}
                  />
                  <DateField label={t('startDate')} value={r.startDate} error={errors[`${i}.startDate`]} onChange={(startDate) => set({ startDate })} />
                  <DateField label={t('endDate')} value={r.endDate} error={errors[`${i}.endDate`]} onChange={(endDate) => set({ endDate })} />
                  <Button type="button" variant="ghost" size="sm" style={{ marginBlockStart: 28 }} onClick={() => setRows(rows.filter((_, j) => j !== i))}>{t('remove')}</Button>
                </div>
              </fieldset>
            );
          })}
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <Button type="button" variant="secondary" onClick={() => setRows([...rows, { teacherId: '', role: rows.length ? 'assistant' : 'primary', startDate: '', endDate: '' }])}>{t('add')}</Button>
            <Button type="submit" loading={mutation.busy}>{t('save')}</Button>
            <Button type="button" variant="secondary" onClick={() => setRows(null)}>{common('cancel')}</Button>
          </div>
        </form>
      )}
    </Card>
  );
}

/* ======================= roster ======================= */

function RosterPanel({ cls, onChanged }: { cls: ClassSection; onChanged: () => void }) {
  const t = useTranslations('academic.roster');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  const { can } = useSession();
  const canManage = can('enrollments.manage');
  const [showInactive, setShowInactive] = useState(false);
  const roster = useApiQuery(`roster|${cls.id}|${showInactive}`, (api) => api.academic.classes.roster(cls.id, showInactive));
  const candidates = useApiQuery<Student[]>(`roster-students|${cls.branchId}|${can('students.read')}`, async (api) =>
    can('students.read') ? (await api.people('students').list({ pageSize: 100 })).items.filter((s) => s.branchId === cls.branchId) : [],
  );
  const [studentId, setStudentId] = useState('');
  const [enrolledOn, setEnrolledOn] = useState('');
  const [notice, setNotice] = useState<string | null>(null);
  const [withdrawing, setWithdrawing] = useState<{ id: string; name: string } | null>(null);
  const enroll = useApiMutation((api, sid: string, on: string) => api.academic.enrollments.enroll({ classId: cls.id, studentId: sid, enrolledOn: on || null }));
  const withdraw = useApiMutation(async (api, enrollmentId: string) => {
    const e = await api.academic.enrollments.get(enrollmentId);
    return api.academic.enrollments.withdraw(enrollmentId, { rowVersion: e.rowVersion });
  });
  const enrolledIds = new Set((roster.data ?? []).filter((r) => r.status === 'active').map((r) => r.studentId));
  const errors = useFieldErrors(enroll.error);
  const full = cls.enrolledCount >= cls.capacity;
  return (
    <Card aria-labelledby="roster-heading">
      <h2 id="roster-heading" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('title')}</h2>
      <p style={{ fontSize: '0.875rem' }} data-testid="capacity">
        {t('capacity', { count: labels.number(cls.enrolledCount, 0), capacity: labels.number(cls.capacity, 0) })}
        {full && <> <Badge tone="warning">{t('full')}</Badge></>}
      </p>
      {notice && <Alert tone="success" role="status">{notice}</Alert>}
      {canManage && (
        <form
          aria-label={t('enrollTitle')}
          style={{ marginBlock: 12 }}
          onSubmit={async (e) => {
            e.preventDefault();
            setNotice(null);
            const result = await enroll.run(studentId, enrolledOn);
            if (result.ok) {
              setStudentId('');
              setNotice(t('enrolled', { name: personName(result.value) }));
              roster.reload();
              onChanged();
            }
          }}
        >
          <ErrorAlert error={enroll.error} />
          <ConflictDetails error={enroll.error} />
          <FormGrid>
            <SelectField
              label={t('student')} name="studentId" required value={studentId} error={errors.studentId} placeholder={t('chooseStudent')}
              hint={t('studentHint')} onChange={(e) => setStudentId(e.target.value)}
              options={(candidates.data ?? []).filter((s) => !enrolledIds.has(s.id)).map((s) => ({ value: s.id, label: `${personName(s)} (${s.studentNumber})` }))}
            />
            <DateField label={t('enrolledOn')} name="enrolledOn" value={enrolledOn} error={errors.enrolledOn} hint={t('enrolledOnHint')} onChange={setEnrolledOn} />
          </FormGrid>
          <div style={{ marginBlockStart: 12 }}>
            <Button type="submit" loading={enroll.busy} disabled={!studentId}>{t('enroll')}</Button>
          </div>
        </form>
      )}
      <label style={{ display: 'inline-flex', gap: 6, alignItems: 'center', marginBlockEnd: 8 }}>
        <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
        {t('showInactive')}
      </label>
      {roster.error && !roster.data ? (
        <LoadError error={roster.error} onRetry={roster.reload} />
      ) : (
        <DataTable
          caption={t('title')}
          rows={roster.data ?? []}
          loading={roster.loading}
          loadingLabel={common('loading')}
          getRowKey={(r) => r.enrollmentId}
          empty={<StatePanel title={t('empty')} />}
          columns={[
            { key: 'name', header: t('student'), isRowHeader: true, cell: (r) => personName(r) },
            { key: 'number', header: t('studentNumber'), cell: (r) => <span className="eemp-ltr">{r.studentNumber}</span> },
            { key: 'on', header: t('enrolledOn'), cell: (r) => format.calendarDate(r.enrolledOn) },
            { key: 'status', header: common('status'), cell: (r) => labels.enrollmentStatus(r.status) },
            {
              key: 'actions', header: common('actions'),
              cell: (r) => canManage && r.status === 'active' && (
                <Button size="sm" variant="secondary" onClick={() => { withdraw.reset(); setWithdrawing({ id: r.enrollmentId, name: personName(r) }); }}>
                  {t('withdraw')}
                </Button>
              ),
            },
          ]}
        />
      )}
      <ConfirmDialog
        open={withdrawing !== null}
        title={t('withdrawTitle', { name: withdrawing?.name ?? '' })}
        description={t('withdrawBody')}
        confirmLabel={t('withdraw')}
        cancelLabel={common('cancel')}
        busy={withdraw.busy}
        onCancel={() => setWithdrawing(null)}
        onConfirm={async () => {
          if (!withdrawing) return;
          const result = await withdraw.run(withdrawing.id);
          if (result.ok) {
            setWithdrawing(null);
            roster.reload();
            onChanged();
          }
        }}
      >
        <ErrorAlert error={withdraw.error} />
      </ConfirmDialog>
    </Card>
  );
}
