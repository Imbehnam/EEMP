'use client';

import {
  GENDERS, PERSON_STATUSES,
  type Branch, type Guardian, type PersonBase, type PersonByKind, type PersonKind, type Staff, type Student,
  type StudentGuardianLink, type Teacher,
} from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { Link, useRouter } from '@eemp/i18n/navigation';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Alert, Badge, Button, Card, ConfirmDialog, DescriptionList, FormGrid, PageHeader, SelectField, Spinner, TextField } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';

import { BackLink, CreateButton, FormActions, Notice, nullIfEmpty, PagedList, useFieldErrors } from '../components/common';

export const RELATIONSHIPS = ['father', 'mother', 'legalGuardian', 'other'] as const;

/** Branch lookup for labels and the branch select (only branches the user may read). */
function useBranches() {
  const { can } = useSession();
  return useApiQuery('branch-lookup', async (api) =>
    can('branches.read') ? (await api.branches.list({ pageSize: 100 })).items : ([] as Branch[]),
  );
}

export function PersonStatusBadge({ status }: { status: string }) {
  const t = useTranslations('people');
  return <Badge tone={status === 'active' ? 'success' : 'neutral'}>{t('statusLabel', { status })}</Badge>;
}

const numberOf = (kind: PersonKind, p: PersonBase) =>
  kind === 'students' ? (p as Student).studentNumber : kind === 'teachers' || kind === 'staff' ? (p as Teacher).employeeNumber : null;

export function PeopleListScreen({ kind, notice }: { kind: PersonKind; notice?: string }) {
  const t = useTranslations('people');
  const common = useTranslations('common');
  const { can } = useSession();
  const branches = useBranches();
  const branchName = (id: string) => branches.data?.find((b) => b.id === id)?.name ?? common('none');
  return (
    <>
      <PageHeader
        title={t('title', { kind })}
        description={t('intro', { kind })}
        actions={can(`${kind}.manage`) && <CreateButton href={`/people/${kind}/new`} label={t('new', { kind })} />}
      />
      <Notice notice={notice} />
      <PagedList<PersonBase>
        queryKey={`people-${kind}`}
        load={(api, q) => api.people(kind).list(q) as never}
        caption={t('title', { kind })}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        getRowKey={(p) => p.id}
        columns={[
          {
            key: 'name', header: t('fullName'), isRowHeader: true,
            cell: (p) => <Link href={`/people/${kind}/${p.id}`}>{`${p.firstName} ${p.lastName}`}</Link>,
          },
          ...(kind !== 'guardians'
            ? [{ key: 'number', header: kind === 'students' ? t('studentNumber') : t('employeeNumber'), cell: (p: PersonBase) => <span className="eemp-ltr">{numberOf(kind, p)}</span> }]
            : [{ key: 'phone', header: t('phone'), cell: (p: PersonBase) => (p.phone ? <span className="eemp-ltr">{p.phone}</span> : common('none')) }]),
          { key: 'branch', header: t('branch'), cell: (p) => branchName(p.branchId) },
          { key: 'status', header: t('status'), cell: (p) => <PersonStatusBadge status={p.status} /> },
        ]}
      />
    </>
  );
}

interface PersonValues {
  branchId: string;
  firstName: string;
  lastName: string;
  nationalId: string;
  birthDate: string;
  gender: string;
  phone: string;
  email: string;
  status: string;
  studentNumber: string;
  employeeNumber: string;
  subjects: string;
  position: string;
}

const emptyValues: PersonValues = {
  branchId: '', firstName: '', lastName: '', nationalId: '', birthDate: '', gender: '', phone: '', email: '', status: 'active',
  studentNumber: '', employeeNumber: '', subjects: '', position: '',
};

function toValues(p: PersonBase): PersonValues {
  return {
    branchId: p.branchId, firstName: p.firstName, lastName: p.lastName, nationalId: p.nationalId ?? '', birthDate: p.birthDate ?? '',
    gender: p.gender ?? '', phone: p.phone ?? '', email: p.email ?? '', status: p.status,
    studentNumber: (p as Student).studentNumber ?? '', employeeNumber: (p as Teacher).employeeNumber ?? '',
    subjects: ((p as Teacher).subjects ?? []).join(', '), position: (p as Staff).position ?? '',
  };
}

/** Builds the kind-specific payload (create and update share it). */
function toPayload(kind: PersonKind, v: PersonValues, isCreate: boolean) {
  const base = {
    branchId: v.branchId, firstName: v.firstName.trim(), lastName: v.lastName.trim(), nationalId: nullIfEmpty(v.nationalId),
    birthDate: nullIfEmpty(v.birthDate), gender: (nullIfEmpty(v.gender) as PersonBase['gender']) ?? null, phone: nullIfEmpty(v.phone),
    email: nullIfEmpty(v.email), status: v.status as PersonBase['status'],
  };
  switch (kind) {
    case 'students':
      // studentNumber is optional on create: the server generates one when omitted.
      return { ...base, ...(isCreate && !v.studentNumber.trim() ? {} : { studentNumber: v.studentNumber.trim() }) };
    case 'teachers':
      return { ...base, employeeNumber: v.employeeNumber.trim(), subjects: v.subjects.split(/[,،]/).map((s) => s.trim()).filter(Boolean) };
    case 'staff':
      return { ...base, employeeNumber: v.employeeNumber.trim(), position: v.position.trim() };
    default:
      return base;
  }
}

function PersonFields({
  kind, v, set, errors, branches, isCreate,
}: { kind: PersonKind; v: PersonValues; set: (v: PersonValues) => void; errors: Record<string, string>; branches: Branch[]; isCreate: boolean }) {
  const t = useTranslations('people');
  const format = useLocaleFormat();
  const field = (key: keyof PersonValues) => ({ value: v[key], error: errors[key], onChange: (e: { target: { value: string } }) => set({ ...v, [key]: e.target.value }) });
  return (
    <FormGrid>
      <SelectField label={t('branch')} required placeholder={t('chooseBranch')} options={branches.map((b) => ({ value: b.id, label: b.name }))} {...field('branchId')} />
      <TextField label={t('firstName')} required autoComplete="off" {...field('firstName')} />
      <TextField label={t('lastName')} required autoComplete="off" {...field('lastName')} />
      {kind === 'students' && (
        <TextField label={t('studentNumber')} hint={isCreate ? t('studentNumberHint') : undefined} dir="ltr" required={!isCreate} {...field('studentNumber')} />
      )}
      {(kind === 'teachers' || kind === 'staff') && <TextField label={t('employeeNumber')} dir="ltr" required {...field('employeeNumber')} />}
      {kind === 'staff' && <TextField label={t('position')} required {...field('position')} />}
      {kind === 'teachers' && <TextField label={t('subjects')} hint={t('subjectsHint')} {...field('subjects')} />}
      <TextField label={t('nationalId')} dir="ltr" inputMode="numeric" maxLength={10} {...field('nationalId')} />
      <TextField
        label={t('birthDate')}
        type="date"
        dir="ltr"
        hint={v.birthDate ? `${t('birthDateHint')} ${format.calendarDate(v.birthDate)}` : t('birthDateHint')}
        {...field('birthDate')}
      />
      <SelectField
        label={t('gender')}
        placeholder={t('unspecified')}
        options={GENDERS.map((g) => ({ value: g, label: t('genderLabel', { gender: g }) }))}
        {...field('gender')}
      />
      <TextField label={t('phone')} type="tel" dir="ltr" {...field('phone')} />
      <TextField label={t('email')} type="email" dir="ltr" {...field('email')} />
      <SelectField label={t('status')} options={PERSON_STATUSES.map((s) => ({ value: s, label: t('statusLabel', { status: s }) }))} {...field('status')} />
    </FormGrid>
  );
}

export function PersonCreateScreen({ kind }: { kind: PersonKind }) {
  const t = useTranslations('people');
  const common = useTranslations('common');
  const router = useRouter();
  const branches = useBranches();
  const [v, setV] = useState(emptyValues);
  const mutation = useApiMutation((api, body: ReturnType<typeof toPayload>) => api.people(kind).create(body as never) as Promise<PersonBase>);
  const errors = useFieldErrors(mutation.error);
  if (branches.error) return <LoadError error={branches.error} onRetry={branches.reload} />;
  if (!branches.data) return <Spinner label={common('loading')} />;
  return (
    <>
      <PageHeader before={<BackLink href={`/people/${kind}`} />} title={t('createTitle', { kind })} />
      <Card>
        <ErrorAlert error={mutation.error} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            const result = await mutation.run(toPayload(kind, v, true));
            if (result.ok) router.push(`/people/${kind}/${result.value.id}?notice=created`);
          }}
        >
          <PersonFields kind={kind} v={v} set={setV} errors={errors} branches={branches.data} isCreate />
          <FormActions cancelHref={`/people/${kind}`} busy={mutation.busy} />
        </form>
      </Card>
    </>
  );
}

/** Guardian links for a student (GET/PUT /students/{id}/guardians). One primary contact at most. */
export function GuardianLinksEditor({ student, readOnly }: { student: Student; readOnly?: boolean }) {
  const t = useTranslations('people');
  const common = useTranslations('common');
  const guardians = useApiQuery('guardian-lookup', async (api) => (await api.people('guardians').list({ pageSize: 100 })).items);
  const links = useApiQuery(`student-guardians|${student.id}`, (api) => api.studentGuardians.get(student.id));
  const [rows, setRows] = useState<StudentGuardianLink[] | null>(null);
  const [saved, setSaved] = useState(false);
  const mutation = useApiMutation((api, list: StudentGuardianLink[]) => api.studentGuardians.set(student.id, list));
  const errors = useFieldErrors(mutation.error);

  if (links.error) return <LoadError error={links.error} onRetry={links.reload} />;
  if (!links.data) return <Spinner label={common('loading')} />;
  const current = rows ?? links.data;
  const options: Guardian[] = guardians.data ?? [];
  const update = (index: number, patch: Partial<StudentGuardianLink>) =>
    setRows(current.map((r, i) => (i === index ? { ...r, ...patch } : patch.isPrimary ? { ...r, isPrimary: false } : r)));
  const guardianName = (id: string) => {
    const g = options.find((o) => o.id === id);
    return g ? `${g.firstName} ${g.lastName}` : id;
  };

  return (
    <section aria-labelledby="guardians-heading">
      <h2 id="guardians-heading" style={{ fontSize: '1.125rem', marginBlock: 0 }}>{t('guardians')}</h2>
      <p style={{ color: 'var(--eemp-color-fg-muted)', marginBlock: '4px 12px' }}>{t('guardiansHint')}</p>
      {saved && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{t('guardiansSaved')}</Alert>}
      <ErrorAlert error={mutation.error} />
      {errors.guardians && <Alert tone="danger">{errors.guardians}</Alert>}
      {!!guardians.error && <ErrorAlert error={guardians.error} />}
      {current.length === 0 && <p>{t('noGuardians')}</p>}
      <ol style={{ listStyle: 'none', padding: 0, margin: 0, display: 'grid', gap: 12 }}>
        {current.map((row, index) => (
          <li key={index} style={{ display: 'grid', gap: 8, gridTemplateColumns: 'repeat(auto-fit, minmax(11rem, 1fr)) auto', alignItems: 'end' }}>
            {readOnly ? (
              <DescriptionList>
                <dt>{t('guardian')}</dt>
                <dd>{guardianName(row.guardianId)}</dd>
                <dt>{t('relationship')}</dt>
                <dd>{t('relationshipLabel', { relationship: row.relationship })}</dd>
              </DescriptionList>
            ) : (
              <>
                <SelectField
                  label={t('guardian')}
                  value={row.guardianId}
                  placeholder={t('chooseGuardian')}
                  options={options.map((g) => ({ value: g.id, label: `${g.firstName} ${g.lastName}` }))}
                  onChange={(e) => update(index, { guardianId: e.target.value })}
                />
                <SelectField
                  label={t('relationship')}
                  value={row.relationship}
                  options={RELATIONSHIPS.map((r) => ({ value: r, label: t('relationshipLabel', { relationship: r }) }))}
                  onChange={(e) => update(index, { relationship: e.target.value })}
                />
                <label style={{ display: 'inline-flex', gap: 6, alignItems: 'center', minBlockSize: 40 }}>
                  <input type="radio" name="primary-guardian" checked={row.isPrimary} onChange={() => update(index, { isPrimary: true })} />
                  {t('isPrimary')}
                </label>
                <Button variant="ghost" aria-label={t('removeGuardian', { index: index + 1 })} onClick={() => setRows(current.filter((_, i) => i !== index))}>
                  <span aria-hidden="true">{'✕'}</span>
                </Button>
              </>
            )}
          </li>
        ))}
      </ol>
      {!readOnly && (
        <div style={{ display: 'flex', gap: 8, marginBlockStart: 12, flexWrap: 'wrap' }}>
          <Button
            variant="secondary"
            onClick={() => setRows([...current, { guardianId: '', relationship: 'father', isPrimary: current.length === 0 }])}
          >
            {t('addGuardian')}
          </Button>
          <Button
            loading={mutation.busy}
            onClick={async () => {
              setSaved(false);
              const result = await mutation.run(current.filter((r) => r.guardianId));
              if (result.ok) {
                setSaved(true);
                setRows(null);
                links.reload();
              }
            }}
          >
            {t('saveGuardians')}
          </Button>
        </div>
      )}
    </section>
  );
}

export function PersonEditScreen({ kind, id, notice }: { kind: PersonKind; id: string; notice?: string }) {
  const t = useTranslations('people');
  const common = useTranslations('common');
  const router = useRouter();
  const { can } = useSession();
  const canManage = can(`${kind}.manage`);
  const branches = useBranches();
  const query = useApiQuery(`person|${kind}|${id}`, (api) => api.people(kind).get(id) as Promise<PersonByKind[typeof kind]>);
  const [values, setValues] = useState<PersonValues | null>(null);
  const [saved, setSaved] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const mutation = useApiMutation((api, p: PersonBase, v: PersonValues) =>
    api.people(kind).update(p.id, { ...toPayload(kind, v, false), rowVersion: p.rowVersion } as never) as Promise<PersonBase>,
  );
  const remove = useApiMutation((api, personId: string) => api.people(kind).remove(personId));
  const errors = useFieldErrors(mutation.error);

  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  if (branches.error) return <LoadError error={branches.error} onRetry={branches.reload} />;
  const person = query.data as PersonBase | undefined;
  if (!person || !branches.data) return <Spinner label={common('loading')} />;
  const v = values ?? toValues(person);
  const name = `${person.firstName} ${person.lastName}`;

  return (
    <>
      <PageHeader
        before={<BackLink href={`/people/${kind}`} />}
        title={name}
        description={t('editTitle', { kind })}
        actions={
          <>
            <PersonStatusBadge status={person.status} />
            {canManage && <Button variant="danger" onClick={() => setConfirmDelete(true)}>{common('delete')}</Button>}
          </>
        }
      />
      <Notice notice={saved ? 'saved' : notice} />
      <Card style={{ marginBlockEnd: 16 }}>
        <ErrorAlert error={mutation.error} onReload={() => { mutation.reset(); setValues(null); query.reload(); }} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            setSaved(false);
            const result = await mutation.run(person, v);
            if (result.ok) {
              query.setData(result.value as never);
              setValues(null);
              setSaved(true);
            }
          }}
        >
          <fieldset disabled={!canManage} style={{ border: 0, padding: 0, margin: 0 }}>
            <PersonFields kind={kind} v={v} set={setValues} errors={errors} branches={branches.data} isCreate={false} />
          </fieldset>
          <p style={{ color: 'var(--eemp-color-fg-muted)', fontSize: '0.875rem', marginBlockEnd: 0 }}>
            {t('linkedAccount')}: {person.userId ? <span className="eemp-ltr">{person.userId}</span> : t('noLinkedAccount')}
          </p>
          {canManage && <FormActions cancelHref={`/people/${kind}`} busy={mutation.busy} />}
        </form>
      </Card>
      {kind === 'students' && (
        <Card>
          <GuardianLinksEditor student={person as Student} readOnly={!canManage} />
        </Card>
      )}
      <ConfirmDialog
        open={confirmDelete}
        title={common('confirmDeleteTitle', { name })}
        description={common('confirmDeleteBody')}
        confirmLabel={common('delete')}
        cancelLabel={common('cancel')}
        busy={remove.busy}
        onCancel={() => { remove.reset(); setConfirmDelete(false); }}
        onConfirm={async () => {
          const result = await remove.run(person.id);
          if (result.ok) router.push(`/people/${kind}?notice=deleted`);
        }}
      >
        <ErrorAlert error={remove.error} />
      </ConfirmDialog>
    </>
  );
}
