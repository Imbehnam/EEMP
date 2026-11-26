'use client';

import type { Branch, CreateUserRequest, Role, TenantUser, UserAssignment } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { listTimeZones, localeMetadata, locales, toApiLocale, toSupportedLocale } from '@eemp/i18n';
import { Link, useRouter } from '@eemp/i18n/navigation';
import { Alert, Badge, Button, Card, ConfirmDialog, FormGrid, PageHeader, SelectField, Spinner, TextField } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useMemo, useState, type FormEvent } from 'react';

import { ActiveBadge, BackLink, CreateButton, FormActions, Notice, nullIfEmpty, PagedList, useFieldErrors } from '../components/common';

export function UsersListScreen({ notice }: { notice?: string }) {
  const t = useTranslations('users');
  const common = useTranslations('common');
  const { can } = useSession();
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={can('users.manage') && <CreateButton href="/users/new" label={t('new')} />} />
      <Notice notice={notice} />
      <PagedList<TenantUser>
        queryKey="users"
        load={(api, q) => api.users.list(q)}
        caption={t('title')}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        emptyAction={can('users.manage') && <CreateButton href="/users/new" label={t('new')} />}
        getRowKey={(u) => u.id}
        columns={[
          { key: 'name', header: t('displayName'), isRowHeader: true, cell: (u) => <Link href={`/users/${u.id}`}>{u.displayName}</Link> },
          { key: 'userName', header: t('userName'), cell: (u) => <span className="eemp-ltr">{u.userName}</span> },
          { key: 'email', header: t('email'), cell: (u) => (u.email ? <span className="eemp-ltr">{u.email}</span> : common('none')) },
          {
            key: 'roles',
            header: t('rolesColumn'),
            cell: (u) =>
              u.assignments.length === 0 ? (
                t('noAssignments')
              ) : (
                <span style={{ display: 'inline-flex', gap: 4, flexWrap: 'wrap' }}>
                  {[...new Set(u.assignments.map((a) => a.roleCode))].map((code) => (
                    <Badge key={code} className="eemp-ltr">{code}</Badge>
                  ))}
                </span>
              ),
          },
          { key: 'status', header: common('status'), cell: (u) => <ActiveBadge active={u.isActive} /> },
        ]}
      />
    </>
  );
}

/** Empty means "organization default"; otherwise send the API's locale tag (`fa-IR`). */
const apiLocaleOrNull = (value: string) => (value.trim() ? toApiLocale(value) : null);

type UserValues = { displayName: string; email: string; phone: string; locale: string; timeZone: string };

function UserFields({ v, set, errors }: { v: UserValues; set: (v: UserValues) => void; errors: Record<string, string> }) {
  const t = useTranslations('users');
  const zones = useMemo(() => listTimeZones(), []);
  return (
    <>
      <TextField label={t('displayName')} required value={v.displayName} error={errors.displayName} onChange={(e) => set({ ...v, displayName: e.target.value })} />
      <TextField label={t('email')} type="email" dir="ltr" value={v.email} error={errors.email} onChange={(e) => set({ ...v, email: e.target.value })} />
      <TextField label={t('phone')} type="tel" dir="ltr" value={v.phone} error={errors.phone} onChange={(e) => set({ ...v, phone: e.target.value })} />
      <SelectField
        label={t('locale')}
        value={v.locale}
        error={errors.locale}
        placeholder={t('useDefault')}
        options={locales.map((code) => ({ value: code, label: localeMetadata[code].nativeName, lang: code }))}
        onChange={(e) => set({ ...v, locale: e.target.value })}
      />
      <SelectField
        label={t('timeZone')}
        dir="ltr"
        value={v.timeZone}
        error={errors.timeZone}
        placeholder={t('useDefault')}
        options={zones.map((z) => ({ value: z, label: z }))}
        onChange={(e) => set({ ...v, timeZone: e.target.value })}
      />
    </>
  );
}

export function UserCreateScreen() {
  const t = useTranslations('users');
  const router = useRouter();
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [v, setV] = useState<UserValues>({ displayName: '', email: '', phone: '', locale: '', timeZone: '' });
  const mutation = useApiMutation((api, body: CreateUserRequest) => api.users.create(body));
  const errors = useFieldErrors(mutation.error);
  return (
    <>
      <PageHeader before={<BackLink href="/users" />} title={t('createTitle')} />
      <Card>
        <ErrorAlert error={mutation.error} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            const result = await mutation.run({
              userName: userName.trim(), displayName: v.displayName.trim(), email: nullIfEmpty(v.email), phone: nullIfEmpty(v.phone),
              password, locale: apiLocaleOrNull(v.locale), timeZone: nullIfEmpty(v.timeZone),
            });
            if (result.ok) router.push(`/users/${result.value.id}?notice=created`);
          }}
        >
          <FormGrid>
            <TextField label={t('userName')} dir="ltr" required autoComplete="off" value={userName} error={errors.userName} onChange={(e) => setUserName(e.target.value)} />
            <UserFields v={v} set={setV} errors={errors} />
            <TextField label={t('password')} hint={t('passwordHint')} type="password" dir="ltr" required autoComplete="new-password" value={password} error={errors.password} onChange={(e) => setPassword(e.target.value)} />
          </FormGrid>
          <FormActions cancelHref="/users" busy={mutation.busy} />
        </form>
      </Card>
    </>
  );
}

/** Role assignments editor: each row grants one role for one branch or the whole organization. */
export function AssignmentsEditor({
  user, roles, branches, onSaved, readOnly,
}: { user: TenantUser; roles: Role[]; branches: Branch[]; onSaved: () => void; readOnly?: boolean }) {
  const t = useTranslations('users');
  const [rows, setRows] = useState<UserAssignment[]>(user.assignments.map((a) => ({ roleId: a.roleId, branchId: a.branchId })));
  const [saved, setSaved] = useState(false);
  const mutation = useApiMutation((api, list: UserAssignment[]) => api.users.setAssignments(user.id, list));
  const errors = useFieldErrors(mutation.error);
  const ALL = '__all__';
  return (
    <section aria-labelledby="assignments-heading">
      <h2 id="assignments-heading" style={{ fontSize: '1.125rem', marginBlock: 0 }}>{t('assignments')}</h2>
      <p style={{ color: 'var(--eemp-color-fg-muted)', marginBlock: '4px 12px' }}>{t('assignmentsHint')}</p>
      {saved && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{t('assignmentsSaved')}</Alert>}
      <ErrorAlert error={mutation.error} />
      {errors.assignments && <Alert tone="danger">{errors.assignments}</Alert>}
      {rows.length === 0 && <p>{t('noAssignments')}</p>}
      <ol style={{ listStyle: 'none', padding: 0, margin: 0, display: 'grid', gap: 12 }}>
        {rows.map((row, index) => (
          <li key={index} aria-label={t('assignmentRow', { index: index + 1 })} style={{ display: 'grid', gap: 8, gridTemplateColumns: 'repeat(auto-fit, minmax(12rem, 1fr)) auto', alignItems: 'end' }}>
            <SelectField
              label={t('role')}
              value={row.roleId}
              disabled={readOnly}
              options={roles.map((r) => ({ value: r.id, label: r.name }))}
              onChange={(e) => setRows(rows.map((r, i) => (i === index ? { ...r, roleId: e.target.value } : r)))}
            />
            <SelectField
              label={t('branch')}
              value={row.branchId ?? ALL}
              disabled={readOnly}
              options={[{ value: ALL, label: t('allBranches') }, ...branches.map((b) => ({ value: b.id, label: b.name }))]}
              onChange={(e) => setRows(rows.map((r, i) => (i === index ? { ...r, branchId: e.target.value === ALL ? null : e.target.value } : r)))}
            />
            {!readOnly && (
              <Button variant="ghost" aria-label={t('removeAssignment', { index: index + 1 })} onClick={() => setRows(rows.filter((_, i) => i !== index))}>
                <span aria-hidden="true">{'✕'}</span>
              </Button>
            )}
          </li>
        ))}
      </ol>
      {!readOnly && (
        <div style={{ display: 'flex', gap: 8, marginBlockStart: 12, flexWrap: 'wrap' }}>
          <Button variant="secondary" disabled={roles.length === 0} onClick={() => setRows([...rows, { roleId: roles[0]?.id ?? '', branchId: null }])}>
            {t('addAssignment')}
          </Button>
          <Button
            loading={mutation.busy}
            onClick={async () => {
              setSaved(false);
              const result = await mutation.run(rows);
              if (result.ok) {
                setSaved(true);
                onSaved();
              }
            }}
          >
            {t('saveAssignments')}
          </Button>
        </div>
      )}
    </section>
  );
}

export function UserEditScreen({ id, notice }: { id: string; notice?: string }) {
  const t = useTranslations('users');
  const common = useTranslations('common');
  const { can, user: me } = useSession();
  const canManage = can('users.manage');
  const query = useApiQuery(`user|${id}`, (api) => api.users.get(id));
  const lookups = useApiQuery('user-lookups', async (api) => {
    const [roles, branches] = await Promise.all([
      api.roles.list(),
      can('branches.read') ? api.branches.list({ pageSize: 100 }).then((p) => p.items) : Promise.resolve([] as Branch[]),
    ]);
    return { roles, branches };
  });
  const [values, setValues] = useState<UserValues | null>(null);
  const [saved, setSaved] = useState<string | undefined>(undefined);
  const [confirm, setConfirm] = useState(false);
  const mutation = useApiMutation((api, u: TenantUser, v: UserValues) =>
    api.users.update(u.id, {
      displayName: v.displayName.trim(), email: nullIfEmpty(v.email), phone: nullIfEmpty(v.phone),
      locale: apiLocaleOrNull(v.locale), timeZone: nullIfEmpty(v.timeZone), rowVersion: u.rowVersion,
    }),
  );
  const status = useApiMutation((api, u: TenantUser) => (u.isActive ? api.users.deactivate(u.id) : api.users.activate(u.id)));
  const errors = useFieldErrors(mutation.error);

  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const user = query.data;
  if (!user) return <Spinner label={common('loading')} />;
  const v = values ?? { displayName: user.displayName, email: user.email ?? '', phone: user.phone ?? '', locale: user.locale ? toSupportedLocale(user.locale) : '', timeZone: user.timeZone ?? '' };

  return (
    <>
      <PageHeader
        before={<BackLink href="/users" />}
        title={user.displayName}
        description={<span className="eemp-ltr">{user.userName}</span>}
        actions={
          <>
            <ActiveBadge active={user.isActive} />
            {canManage && user.id !== me.id && (
              <Button variant={user.isActive ? 'danger' : 'secondary'} onClick={() => setConfirm(true)}>
                {user.isActive ? t('deactivate') : t('activate')}
              </Button>
            )}
          </>
        }
      />
      {saved === 'activated' || saved === 'deactivated' ? (
        <Alert tone="success" role="status" style={{ marginBlockEnd: 16 }}>{t(saved)}</Alert>
      ) : (
        <Notice notice={saved ?? notice} />
      )}
      <Card style={{ marginBlockEnd: 16 }}>
        <h2 style={{ fontSize: '1.125rem', marginBlockStart: 0 }}>{t('editTitle')}</h2>
        <ErrorAlert error={mutation.error} onReload={() => { mutation.reset(); setValues(null); query.reload(); }} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            setSaved(undefined);
            const result = await mutation.run(user, v);
            if (result.ok) {
              query.setData(result.value);
              setValues(null);
              setSaved('saved');
            }
          }}
        >
          <fieldset disabled={!canManage} style={{ border: 0, padding: 0, margin: 0 }}>
            <FormGrid>
              <UserFields v={v} set={setValues} errors={errors} />
            </FormGrid>
          </fieldset>
          {canManage && <FormActions cancelHref="/users" busy={mutation.busy} />}
        </form>
      </Card>
      <Card>
        {lookups.error ? (
          <LoadError error={lookups.error} onRetry={lookups.reload} />
        ) : !lookups.data ? (
          <Spinner label={common('loading')} />
        ) : (
          <AssignmentsEditor
            key={user.id}
            user={user}
            roles={lookups.data.roles}
            branches={lookups.data.branches}
            readOnly={!canManage}
            onSaved={query.reload}
          />
        )}
      </Card>
      <ConfirmDialog
        open={confirm}
        tone={user.isActive ? 'danger' : 'primary'}
        title={user.isActive ? t('deactivateTitle', { name: user.displayName }) : t('activate')}
        description={user.isActive ? t('deactivateBody') : undefined}
        confirmLabel={user.isActive ? t('deactivate') : t('activate')}
        cancelLabel={common('cancel')}
        busy={status.busy}
        onCancel={() => { status.reset(); setConfirm(false); }}
        onConfirm={async () => {
          const wasActive = user.isActive;
          const result = await status.run(user);
          if (result.ok) {
            setConfirm(false);
            setSaved(wasActive ? 'deactivated' : 'activated');
            query.reload();
          }
        }}
      >
        <ErrorAlert error={status.error} />
      </ConfirmDialog>
    </>
  );
}
