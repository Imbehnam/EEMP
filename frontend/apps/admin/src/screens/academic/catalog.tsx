'use client';

import {
  ACADEMIC_YEAR_STATUSES, GRADING_SCHEME_KINDS,
  type AcademicYear, type AcademicYearRequest, type AcademicYearStatus, type Course, type CourseRequest, type Endpoints, type GradeBand,
  type GradingScheme, type GradingSchemeKind, type GradingSchemeRequest, type Subject, type SubjectRequest, type Term,
} from '@eemp/api-client';
import { DateField, ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { useAcademicLabels } from '@eemp/app/academic';
import { Link, useRouter } from '@eemp/i18n/navigation';
import {
  Badge, Button, Card, Checkbox, ConfirmDialog, DataTable, FormGrid, PageHeader, SelectField, Spinner, StatePanel, TextAreaField, TextField,
} from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState, type FormEvent, type ReactNode } from 'react';

import { BackLink, CreateButton, FormActions, Notice, nullIfEmpty, PagedList, useFieldErrors } from '../../components/common';
import { DateRange, useAcademicLookups } from './options';

/* ======================= shared editor scaffolding ======================= */

interface EditorConfig<T extends { id: string; rowVersion: string }, V> {
  ns: string;
  listHref: string;
  load: (api: Endpoints, id: string) => Promise<T>;
  update: (api: Endpoints, entity: T, v: V) => Promise<T>;
  remove: (api: Endpoints, id: string) => Promise<unknown>;
  toValues: (entity: T) => V;
  title: (entity: T) => string;
  Fields: (props: { v: V; set: (v: V) => void; errors: Record<string, string>; disabled?: boolean; entity?: T }) => ReactNode;
  after?: (entity: T) => ReactNode;
}

function EntityEditScreen<T extends { id: string; rowVersion: string }, V>({ id, notice, config }: { id: string; notice?: string; config: EditorConfig<T, V> }) {
  const common = useTranslations('common');
  const router = useRouter();
  const { can } = useSession();
  const canManage = can('catalog.manage');
  const query = useApiQuery(`${config.ns}|${id}`, (api) => config.load(api, id));
  const [values, setValues] = useState<V | null>(null);
  const [saved, setSaved] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const mutation = useApiMutation((api, e: T, v: V) => config.update(api, e, v));
  const remove = useApiMutation((api, entityId: string) => config.remove(api, entityId));
  const errors = useFieldErrors(mutation.error);
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const entity = query.data;
  if (!entity) return <Spinner label={common('loading')} />;
  const v = values ?? config.toValues(entity);
  const Fields = config.Fields;
  return (
    <>
      <PageHeader
        before={<BackLink href={config.listHref} />}
        title={config.title(entity)}
        actions={canManage && <Button variant="danger" onClick={() => setConfirmDelete(true)}>{common('delete')}</Button>}
      />
      <Notice notice={saved ? 'saved' : notice} />
      <Card>
        <ErrorAlert error={mutation.error} onReload={() => { mutation.reset(); setValues(null); query.reload(); }} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            setSaved(false);
            const result = await mutation.run(entity, v);
            if (result.ok) {
              query.setData(result.value);
              setValues(null);
              setSaved(true);
            }
          }}
        >
          <Fields v={v} set={setValues} errors={errors} disabled={!canManage} entity={entity} />
          {canManage && <FormActions cancelHref={config.listHref} busy={mutation.busy} />}
        </form>
      </Card>
      {config.after?.(entity)}
      <ConfirmDialog
        open={confirmDelete}
        title={common('confirmDeleteTitle', { name: config.title(entity) })}
        description={common('confirmDeleteBody')}
        confirmLabel={common('delete')}
        cancelLabel={common('cancel')}
        busy={remove.busy}
        onCancel={() => { remove.reset(); setConfirmDelete(false); }}
        onConfirm={async () => {
          const result = await remove.run(entity.id);
          if (result.ok) router.push(`${config.listHref}?notice=deleted`);
        }}
      >
        <ErrorAlert error={remove.error} />
      </ConfirmDialog>
    </>
  );
}

function EntityCreateScreen<T extends { id: string }, V>({
  ns, listHref, initial, create, Fields, detailHref,
}: {
  ns: string; listHref: string; initial: V; create: (api: Endpoints, v: V) => Promise<T>;
  Fields: (props: { v: V; set: (v: V) => void; errors: Record<string, string> }) => ReactNode; detailHref: (e: T) => string;
}) {
  const t = useTranslations(ns);
  const router = useRouter();
  const [v, setV] = useState(initial);
  const mutation = useApiMutation((api, values: V) => create(api, values));
  const errors = useFieldErrors(mutation.error);
  return (
    <>
      <PageHeader before={<BackLink href={listHref} />} title={t('createTitle')} />
      <Card>
        <ErrorAlert error={mutation.error} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            const result = await mutation.run(v);
            if (result.ok) router.push(`${detailHref(result.value)}?notice=created`);
          }}
        >
          <Fields v={v} set={setV} errors={errors} />
          <FormActions cancelHref={listHref} busy={mutation.busy} />
        </form>
      </Card>
    </>
  );
}

const fieldsetStyle = { border: 0, padding: 0, margin: 0 } as const;

/* ======================= academic years (+ terms) ======================= */

type YearValues = { name: string; startDate: string; endDate: string; status: AcademicYearStatus };
const yearToRequest = (v: YearValues): AcademicYearRequest => ({ name: v.name.trim(), startDate: v.startDate, endDate: v.endDate, status: v.status });

function YearFields({ v, set, errors, disabled }: { v: YearValues; set: (v: YearValues) => void; errors: Record<string, string>; disabled?: boolean }) {
  const t = useTranslations('academic.years');
  const labels = useAcademicLabels();
  return (
    <fieldset disabled={disabled} style={fieldsetStyle}>
      <FormGrid>
        <TextField label={t('name')} name="name" hint={t('nameHint')} required value={v.name} error={errors.name} onChange={(e) => set({ ...v, name: e.target.value })} />
        <SelectField
          label={t('status')} name="status" value={v.status} error={errors.status}
          onChange={(e) => set({ ...v, status: e.target.value as AcademicYearStatus })}
          options={ACADEMIC_YEAR_STATUSES.map((s) => ({ value: s, label: labels.yearStatus(s) }))}
        />
        <DateField label={t('startDate')} name="startDate" required value={v.startDate} error={errors.startDate} onChange={(startDate) => set({ ...v, startDate })} disabled={disabled} />
        <DateField label={t('endDate')} name="endDate" required value={v.endDate} error={errors.endDate} onChange={(endDate) => set({ ...v, endDate })} disabled={disabled} />
      </FormGrid>
    </fieldset>
  );
}

export function YearsListScreen({ notice }: { notice?: string }) {
  const t = useTranslations('academic.years');
  const labels = useAcademicLabels();
  const { can } = useSession();
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={can('catalog.manage') && <CreateButton href="/academic/years/new" label={t('new')} />} />
      <Notice notice={notice} />
      <PagedList<AcademicYear>
        queryKey="academic-years"
        load={(api, q) => api.academic.years.list(q)}
        caption={t('title')}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        getRowKey={(y) => y.id}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (y) => <Link href={`/academic/years/${y.id}`}>{y.name}</Link> },
          { key: 'dates', header: t('dates'), cell: (y) => <DateRange start={y.startDate} end={y.endDate} /> },
          { key: 'status', header: t('status'), cell: (y) => <Badge tone={y.status === 'active' ? 'success' : 'neutral'}>{labels.yearStatus(y.status)}</Badge> },
        ]}
      />
    </>
  );
}

export function YearCreateScreen() {
  return (
    <EntityCreateScreen<AcademicYear, YearValues>
      ns="academic.years"
      listHref="/academic/years"
      initial={{ name: '', startDate: '', endDate: '', status: 'planned' }}
      create={(api, v) => api.academic.years.create(yearToRequest(v))}
      Fields={YearFields}
      detailHref={(y) => `/academic/years/${y.id}`}
    />
  );
}

export function YearEditScreen({ id, notice }: { id: string; notice?: string }) {
  return (
    <EntityEditScreen<AcademicYear, YearValues>
      id={id}
      notice={notice}
      config={{
        ns: 'year',
        listHref: '/academic/years',
        load: (api, yearId) => api.academic.years.get(yearId),
        update: (api, y, v) => api.academic.years.update(y.id, { ...yearToRequest(v), rowVersion: y.rowVersion }),
        remove: (api, yearId) => api.academic.years.remove(yearId),
        toValues: (y) => ({ name: y.name, startDate: y.startDate, endDate: y.endDate, status: y.status as AcademicYearStatus }),
        title: (y) => y.name,
        Fields: YearFields,
        after: (y) => <TermsPanel year={y} />,
      }}
    />
  );
}

type TermValues = { name: string; startDate: string; endDate: string; sortOrder: string };

function TermsPanel({ year }: { year: AcademicYear }) {
  const t = useTranslations('academic.terms');
  const common = useTranslations('common');
  const { can } = useSession();
  const canManage = can('catalog.manage');
  const query = useApiQuery(`terms|${year.id}`, (api) => api.academic.terms.list(year.id));
  const [editing, setEditing] = useState<Term | 'new' | null>(null);
  const [v, setV] = useState<TermValues>({ name: '', startDate: '', endDate: '', sortOrder: '' });
  const [toDelete, setToDelete] = useState<Term | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const save = useApiMutation((api, target: Term | 'new', values: TermValues) => {
    const body = {
      academicYearId: year.id, name: values.name.trim(), startDate: values.startDate, endDate: values.endDate,
      sortOrder: values.sortOrder ? Number(values.sortOrder) : undefined,
    };
    return target === 'new' ? api.academic.terms.create(body) : api.academic.terms.update(target.id, { ...body, rowVersion: target.rowVersion });
  });
  const remove = useApiMutation((api, termId: string) => api.academic.terms.remove(termId));
  const errors = useFieldErrors(save.error);
  const open = (term: Term | 'new') => {
    save.reset();
    setNotice(null);
    setEditing(term);
    setV(term === 'new'
      ? { name: '', startDate: '', endDate: '', sortOrder: '' }
      : { name: term.name, startDate: term.startDate, endDate: term.endDate, sortOrder: String(term.sortOrder) });
  };
  return (
    <Card aria-labelledby="terms-heading" style={{ marginBlockStart: 16 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <h2 id="terms-heading" style={{ fontSize: '1.0625rem', margin: 0 }}>{t('title')}</h2>
        {canManage && editing === null && <Button onClick={() => open('new')}>{t('new')}</Button>}
      </div>
      <p style={{ fontSize: '0.875rem' }}>{t('intro')}</p>
      {notice && <p role="status">{notice}</p>}
      {editing !== null && (
        <form
          aria-label={editing === 'new' ? t('new') : t('editTitle')}
          style={{ marginBlock: 12 }}
          onSubmit={async (e) => {
            e.preventDefault();
            const result = await save.run(editing, v);
            if (result.ok) {
              setEditing(null);
              setNotice(common('saved'));
              query.reload();
            }
          }}
        >
          <ErrorAlert error={save.error} onReload={() => { save.reset(); query.reload(); setEditing(null); }} />
          <FormGrid>
            <TextField label={t('name')} name="termName" required value={v.name} error={errors.name} onChange={(e) => setV({ ...v, name: e.target.value })} />
            <TextField label={t('sortOrder')} name="sortOrder" type="number" dir="ltr" value={v.sortOrder} error={errors.sortOrder} onChange={(e) => setV({ ...v, sortOrder: e.target.value })} />
            <DateField label={t('startDate')} name="termStart" required value={v.startDate} error={errors.startDate} onChange={(startDate) => setV({ ...v, startDate })} />
            <DateField label={t('endDate')} name="termEnd" required value={v.endDate} error={errors.endDate} onChange={(endDate) => setV({ ...v, endDate })} />
          </FormGrid>
          <div style={{ display: 'flex', gap: 8, marginBlockStart: 12 }}>
            <Button type="submit" loading={save.busy}>{common('save')}</Button>
            <Button type="button" variant="secondary" onClick={() => setEditing(null)}>{common('cancel')}</Button>
          </div>
        </form>
      )}
      <DataTable
        caption={t('title')}
        rows={query.data ?? []}
        loading={query.loading}
        loadingLabel={common('loading')}
        getRowKey={(term) => term.id}
        empty={<StatePanel title={t('empty')} />}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (term) => term.name },
          { key: 'dates', header: t('dates'), cell: (term) => <DateRange start={term.startDate} end={term.endDate} /> },
          {
            key: 'actions', header: common('actions'),
            cell: (term) => canManage && (
              <span style={{ display: 'inline-flex', gap: 6 }}>
                <Button size="sm" variant="secondary" onClick={() => open(term)} aria-label={`${common('edit')} ${term.name}`}>{common('edit')}</Button>
                <Button size="sm" variant="danger" onClick={() => { remove.reset(); setToDelete(term); }} aria-label={`${common('delete')} ${term.name}`}>{common('delete')}</Button>
              </span>
            ),
          },
        ]}
      />
      <ConfirmDialog
        open={toDelete !== null}
        title={common('confirmDeleteTitle', { name: toDelete?.name ?? '' })}
        description={common('confirmDeleteBody')}
        confirmLabel={common('delete')}
        cancelLabel={common('cancel')}
        busy={remove.busy}
        onCancel={() => setToDelete(null)}
        onConfirm={async () => {
          if (!toDelete) return;
          const result = await remove.run(toDelete.id);
          if (result.ok) {
            setToDelete(null);
            query.reload();
          }
        }}
      >
        <ErrorAlert error={remove.error} />
      </ConfirmDialog>
    </Card>
  );
}

/* ======================= subjects ======================= */

type SubjectValues = { code: string; name: string; description: string };
const subjectToRequest = (v: SubjectValues): SubjectRequest => ({ code: v.code.trim(), name: v.name.trim(), description: nullIfEmpty(v.description) });

function SubjectFields({ v, set, errors, disabled }: { v: SubjectValues; set: (v: SubjectValues) => void; errors: Record<string, string>; disabled?: boolean }) {
  const t = useTranslations('academic.subjects');
  return (
    <fieldset disabled={disabled} style={fieldsetStyle}>
      <FormGrid>
        <TextField label={t('code')} name="code" dir="ltr" required maxLength={30} value={v.code} error={errors.code} onChange={(e) => set({ ...v, code: e.target.value })} />
        <TextField label={t('name')} name="name" required value={v.name} error={errors.name} onChange={(e) => set({ ...v, name: e.target.value })} />
        <TextAreaField label={t('description')} rows={2} value={v.description} error={errors.description} onChange={(e) => set({ ...v, description: e.target.value })} />
      </FormGrid>
    </fieldset>
  );
}

export function SubjectsListScreen({ notice }: { notice?: string }) {
  const t = useTranslations('academic.subjects');
  const { can } = useSession();
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={can('catalog.manage') && <CreateButton href="/academic/subjects/new" label={t('new')} />} />
      <Notice notice={notice} />
      <PagedList<Subject>
        queryKey="subjects"
        load={(api, q) => api.academic.subjects.list(q)}
        caption={t('title')}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        getRowKey={(s) => s.id}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (s) => <Link href={`/academic/subjects/${s.id}`}>{s.name}</Link> },
          { key: 'code', header: t('code'), cell: (s) => <span className="eemp-ltr">{s.code}</span> },
        ]}
      />
    </>
  );
}

export function SubjectCreateScreen() {
  return (
    <EntityCreateScreen<Subject, SubjectValues>
      ns="academic.subjects" listHref="/academic/subjects" initial={{ code: '', name: '', description: '' }}
      create={(api, v) => api.academic.subjects.create(subjectToRequest(v))} Fields={SubjectFields} detailHref={(s) => `/academic/subjects/${s.id}`}
    />
  );
}

export function SubjectEditScreen({ id, notice }: { id: string; notice?: string }) {
  return (
    <EntityEditScreen<Subject, SubjectValues>
      id={id}
      notice={notice}
      config={{
        ns: 'subject', listHref: '/academic/subjects',
        load: (api, x) => api.academic.subjects.get(x),
        update: (api, s, v) => api.academic.subjects.update(s.id, { ...subjectToRequest(v), rowVersion: s.rowVersion }),
        remove: (api, x) => api.academic.subjects.remove(x),
        toValues: (s) => ({ code: s.code, name: s.name, description: s.description ?? '' }),
        title: (s) => s.name,
        Fields: SubjectFields,
      }}
    />
  );
}

/* ======================= courses ======================= */

type CourseValues = { subjectId: string; code: string; name: string; gradeLevel: string; description: string; gradingSchemeId: string };
const courseToRequest = (v: CourseValues): CourseRequest => ({
  subjectId: v.subjectId, code: v.code.trim(), name: v.name.trim(), gradeLevel: nullIfEmpty(v.gradeLevel),
  description: nullIfEmpty(v.description), gradingSchemeId: v.gradingSchemeId || null,
});

function CourseFields({ v, set, errors, disabled }: { v: CourseValues; set: (v: CourseValues) => void; errors: Record<string, string>; disabled?: boolean }) {
  const t = useTranslations('academic.courses');
  const lookups = useAcademicLookups();
  return (
    <fieldset disabled={disabled} style={fieldsetStyle}>
      <FormGrid>
        <SelectField
          label={t('subject')} name="subjectId" required value={v.subjectId} error={errors.subjectId} placeholder={t('chooseSubject')}
          onChange={(e) => set({ ...v, subjectId: e.target.value })}
          options={lookups.subjects.map((s) => ({ value: s.id, label: `${s.name} (${s.code})` }))}
        />
        <TextField label={t('code')} name="code" dir="ltr" required maxLength={30} value={v.code} error={errors.code} onChange={(e) => set({ ...v, code: e.target.value })} />
        <TextField label={t('name')} name="name" required value={v.name} error={errors.name} onChange={(e) => set({ ...v, name: e.target.value })} />
        <TextField label={t('gradeLevel')} name="gradeLevel" value={v.gradeLevel} error={errors.gradeLevel} onChange={(e) => set({ ...v, gradeLevel: e.target.value })} />
        <SelectField
          label={t('gradingScheme')} name="gradingSchemeId" value={v.gradingSchemeId} error={errors.gradingSchemeId} hint={t('gradingSchemeHint')}
          placeholder={t('tenantDefault')} onChange={(e) => set({ ...v, gradingSchemeId: e.target.value })}
          options={lookups.schemes.map((s) => ({ value: s.id, label: `${s.name} (${s.code})` }))}
        />
        <TextAreaField label={t('description')} rows={2} value={v.description} error={errors.description} onChange={(e) => set({ ...v, description: e.target.value })} />
      </FormGrid>
    </fieldset>
  );
}

export function CoursesListScreen({ notice }: { notice?: string }) {
  const t = useTranslations('academic.courses');
  const common = useTranslations('common');
  const { can } = useSession();
  const lookups = useAcademicLookups();
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={can('catalog.manage') && <CreateButton href="/academic/courses/new" label={t('new')} />} />
      <Notice notice={notice} />
      <PagedList<Course>
        queryKey="courses"
        load={(api, q) => api.academic.courses.list(q)}
        caption={t('title')}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        getRowKey={(c) => c.id}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (c) => <Link href={`/academic/courses/${c.id}`}>{c.name}</Link> },
          { key: 'code', header: t('code'), cell: (c) => <span className="eemp-ltr">{c.code}</span> },
          { key: 'subject', header: t('subject'), cell: (c) => lookups.subjects.find((s) => s.id === c.subjectId)?.name ?? common('none') },
          { key: 'grade', header: t('gradeLevel'), cell: (c) => c.gradeLevel || common('none') },
        ]}
      />
    </>
  );
}

export function CourseCreateScreen() {
  return (
    <EntityCreateScreen<Course, CourseValues>
      ns="academic.courses" listHref="/academic/courses"
      initial={{ subjectId: '', code: '', name: '', gradeLevel: '', description: '', gradingSchemeId: '' }}
      create={(api, v) => api.academic.courses.create(courseToRequest(v))} Fields={CourseFields} detailHref={(c) => `/academic/courses/${c.id}`}
    />
  );
}

export function CourseEditScreen({ id, notice }: { id: string; notice?: string }) {
  return (
    <EntityEditScreen<Course, CourseValues>
      id={id}
      notice={notice}
      config={{
        ns: 'course', listHref: '/academic/courses',
        load: (api, x) => api.academic.courses.get(x),
        update: (api, c, v) => api.academic.courses.update(c.id, { ...courseToRequest(v), rowVersion: c.rowVersion }),
        remove: (api, x) => api.academic.courses.remove(x),
        toValues: (c) => ({
          subjectId: c.subjectId, code: c.code, name: c.name, gradeLevel: c.gradeLevel ?? '', description: c.description ?? '', gradingSchemeId: c.gradingSchemeId ?? '',
        }),
        title: (c) => c.name,
        Fields: CourseFields,
      }}
    />
  );
}

/* ======================= grading schemes ======================= */

type SchemeValues = {
  code: string; name: string; kind: GradingSchemeKind; minScore: string; maxScore: string; passScore: string; decimalPlaces: string;
  missingCountsAsZero: boolean; isDefault: boolean; bands: { label: string; minValue: string }[];
};
const schemeToRequest = (v: SchemeValues): GradingSchemeRequest => ({
  code: v.code.trim(), name: v.name.trim(), kind: v.kind, minScore: Number(v.minScore), maxScore: Number(v.maxScore), passScore: Number(v.passScore),
  decimalPlaces: Number(v.decimalPlaces), missingCountsAsZero: v.missingCountsAsZero, isDefault: v.isDefault,
  bands: v.bands.filter((b) => b.label.trim() !== '').map<GradeBand>((b) => ({ label: b.label.trim(), minValue: Number(b.minValue) })),
});

function SchemeFields({ v, set, errors, disabled }: { v: SchemeValues; set: (v: SchemeValues) => void; errors: Record<string, string>; disabled?: boolean }) {
  const t = useTranslations('academic.schemes');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const num = (key: 'minScore' | 'maxScore' | 'passScore' | 'decimalPlaces', label: string) => (
    <TextField label={label} name={key} type="number" step="0.01" dir="ltr" required value={v[key]} error={errors[key]} onChange={(e) => set({ ...v, [key]: e.target.value })} />
  );
  return (
    <fieldset disabled={disabled} style={fieldsetStyle}>
      <FormGrid>
        <TextField label={t('code')} name="code" dir="ltr" required maxLength={30} value={v.code} error={errors.code} onChange={(e) => set({ ...v, code: e.target.value })} />
        <TextField label={t('name')} name="name" required value={v.name} error={errors.name} onChange={(e) => set({ ...v, name: e.target.value })} />
        <SelectField
          label={t('kind')} name="kind" value={v.kind} error={errors.kind} onChange={(e) => set({ ...v, kind: e.target.value as GradingSchemeKind })}
          options={GRADING_SCHEME_KINDS.map((k) => ({ value: k, label: labels.schemeKind(k) }))}
        />
        {num('minScore', t('minScore'))}
        {num('maxScore', t('maxScore'))}
        {num('passScore', t('passScore'))}
        {num('decimalPlaces', t('decimalPlaces'))}
        <Checkbox label={t('missingCountsAsZero')} hint={t('missingCountsAsZeroHint')} checked={v.missingCountsAsZero} onChange={(e) => set({ ...v, missingCountsAsZero: e.target.checked })} />
        <Checkbox label={t('isDefault')} hint={t('isDefaultHint')} checked={v.isDefault} onChange={(e) => set({ ...v, isDefault: e.target.checked })} />
      </FormGrid>
      <fieldset style={{ ...fieldsetStyle, marginBlockStart: 16 }}>
        <legend style={{ fontWeight: 600 }}>{t('bands')}</legend>
        <p style={{ fontSize: '0.875rem', marginBlock: 4 }}>{t('bandsHint')}</p>
        {errors.bands && <p role="alert" style={{ color: 'var(--eemp-color-danger)' }}>{errors.bands}</p>}
        {v.bands.map((b, i) => (
          <div key={i} style={{ display: 'flex', gap: 8, alignItems: 'end', marginBlockEnd: 8, flexWrap: 'wrap' }}>
            <TextField
              label={t('bandLabel')} value={b.label} maxLength={30} hint={b.label ? labels.band(b.label) : undefined}
              onChange={(e) => set({ ...v, bands: v.bands.map((x, j) => (j === i ? { ...x, label: e.target.value } : x)) })}
            />
            <TextField
              label={t('bandMin')} type="number" step="0.01" dir="ltr" value={b.minValue}
              onChange={(e) => set({ ...v, bands: v.bands.map((x, j) => (j === i ? { ...x, minValue: e.target.value } : x)) })}
            />
            <Button type="button" variant="ghost" size="sm" onClick={() => set({ ...v, bands: v.bands.filter((_, j) => j !== i) })}>{common('delete')}</Button>
          </div>
        ))}
        <Button type="button" variant="secondary" size="sm" onClick={() => set({ ...v, bands: [...v.bands, { label: '', minValue: '' }] })}>{t('addBand')}</Button>
      </fieldset>
    </fieldset>
  );
}

export function SchemesListScreen({ notice }: { notice?: string }) {
  const t = useTranslations('academic.schemes');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const { can } = useSession();
  const query = useApiQuery('grading-schemes', (api) => api.academic.gradingSchemes.list());
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={can('catalog.manage') && <CreateButton href="/academic/grading-schemes/new" label={t('new')} />} />
      <Notice notice={notice} />
      {query.error && !query.data ? (
        <LoadError error={query.error} onRetry={query.reload} />
      ) : (
        <DataTable<GradingScheme>
          caption={t('title')}
          rows={query.data ?? []}
          loading={query.loading}
          loadingLabel={common('loading')}
          getRowKey={(s) => s.id}
          empty={<StatePanel title={t('empty')} />}
          columns={[
            { key: 'name', header: t('name'), isRowHeader: true, cell: (s) => <Link href={`/academic/grading-schemes/${s.id}`}>{s.name}</Link> },
            { key: 'code', header: t('code'), cell: (s) => <span className="eemp-ltr">{s.code}</span> },
            { key: 'kind', header: t('kind'), cell: (s) => labels.schemeKind(s.kind) },
            { key: 'range', header: t('range'), cell: (s) => t('rangeValue', { min: labels.number(s.minScore), max: labels.number(s.maxScore), pass: labels.number(s.passScore) }) },
            { key: 'default', header: t('isDefault'), cell: (s) => (s.isDefault ? <Badge tone="info">{t('default')}</Badge> : common('none')) },
          ]}
        />
      )}
    </>
  );
}

export function SchemeCreateScreen() {
  return (
    <EntityCreateScreen<GradingScheme, SchemeValues>
      ns="academic.schemes" listHref="/academic/grading-schemes"
      initial={{ code: '', name: '', kind: 'numeric', minScore: '0', maxScore: '20', passScore: '10', decimalPlaces: '2', missingCountsAsZero: true, isDefault: false, bands: [] }}
      create={(api, v) => api.academic.gradingSchemes.create(schemeToRequest(v))} Fields={SchemeFields} detailHref={(s) => `/academic/grading-schemes/${s.id}`}
    />
  );
}

export function SchemeEditScreen({ id, notice }: { id: string; notice?: string }) {
  return (
    <EntityEditScreen<GradingScheme, SchemeValues>
      id={id}
      notice={notice}
      config={{
        ns: 'scheme', listHref: '/academic/grading-schemes',
        load: (api, x) => api.academic.gradingSchemes.get(x),
        update: (api, s, v) => api.academic.gradingSchemes.update(s.id, { ...schemeToRequest(v), rowVersion: s.rowVersion }),
        remove: (api, x) => api.academic.gradingSchemes.remove(x),
        toValues: (s) => ({
          code: s.code, name: s.name, kind: s.kind as GradingSchemeKind, minScore: String(s.minScore), maxScore: String(s.maxScore),
          passScore: String(s.passScore), decimalPlaces: String(s.decimalPlaces), missingCountsAsZero: s.missingCountsAsZero, isDefault: s.isDefault,
          bands: s.bands.map((b) => ({ label: b.label, minValue: String(b.minValue) })),
        }),
        title: (s) => s.name,
        Fields: SchemeFields,
      }}
    />
  );
}
