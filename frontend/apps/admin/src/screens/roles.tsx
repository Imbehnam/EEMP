'use client';

import { groupPermissionsByResource, permissionAction, type Permission, type Role, type RoleInput } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { Link, useRouter } from '@eemp/i18n/navigation';
import { Alert, Badge, Button, Card, ConfirmDialog, DataTable, FormGrid, PageHeader, SearchBox, Spinner, StatePanel, TextField } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useMemo, useState, type FormEvent } from 'react';

import { BackLink, CreateButton, FormActions, Notice, useFieldErrors } from '../components/common';

/** Roles are returned as a plain array; search is client-side. */
export function RolesListScreen({ notice }: { notice?: string }) {
  const t = useTranslations('roles');
  const common = useTranslations('common');
  const { can } = useSession();
  const [search, setSearch] = useState('');
  const query = useApiQuery('roles', (api) => api.roles.list());
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const needle = search.toLocaleLowerCase();
  const rows = (query.data ?? []).filter((r) => !needle || r.name.toLocaleLowerCase().includes(needle) || r.code.toLocaleLowerCase().includes(needle));
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={can('roles.manage') && <CreateButton href="/roles/new" label={t('new')} />} />
      <Notice notice={notice} />
      <div style={{ marginBlockEnd: 12 }}>
        <SearchBox label={t('searchLabel')} placeholder={common('searchPlaceholder')} onSearch={setSearch} debounceMs={0} />
      </div>
      <DataTable<Role>
        caption={t('title')}
        rows={rows}
        loading={query.loading}
        loadingLabel={common('loading')}
        getRowKey={(r) => r.id}
        empty={<StatePanel title={search ? common('noResults', { query: search }) : t('empty')} />}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (r) => <Link href={`/roles/${r.id}`}>{r.name}</Link> },
          { key: 'code', header: t('code'), cell: (r) => <span className="eemp-ltr">{r.code}</span> },
          { key: 'type', header: t('type'), cell: (r) => <Badge tone={r.isSystem ? 'info' : 'neutral'}>{r.isSystem ? t('system') : t('custom')}</Badge> },
          { key: 'perms', header: t('permissions'), cell: (r) => t('permissionCount', { count: r.permissions.length }) },
        ]}
      />
    </>
  );
}

const groupByModule = groupPermissionsByResource;

const actionOf = permissionAction;

/**
 * Permission matrix: one row per module, one column per action (read / manage).
 * Sensitive permissions (personal data, privilege management) are visibly marked.
 */
export function PermissionMatrix({
  permissions, selected, onChange, disabled,
}: { permissions: Permission[]; selected: string[]; onChange: (codes: string[]) => void; disabled?: boolean }) {
  const t = useTranslations('roles');
  const rows = useMemo(() => groupByModule(permissions), [permissions]);
  const actions = useMemo(() => [...new Set(permissions.map((p) => actionOf(p.code)))], [permissions]);
  const set = new Set(selected);
  const toggle = (code: string, on: boolean) => onChange(on ? [...set, code].sort() : selected.filter((c) => c !== code));
  return (
    <div>
      <Alert tone="warning" style={{ marginBlockEnd: 12 }}>
        <Badge tone="danger">{t('sensitive')}</Badge> {t('sensitiveHint')}
      </Alert>
      <div style={{ overflowX: 'auto' }}>
        <table className="eemp-matrix" style={{ inlineSize: '100%', borderCollapse: 'collapse' }}>
          <caption className="eemp-visually-hidden">{t('matrixCaption')}</caption>
          <thead>
            <tr>
              <th scope="col" style={{ textAlign: 'start' }}>{t('moduleColumn')}</th>
              <th scope="col" style={{ textAlign: 'start' }}>{t('scopeColumn')}</th>
              {actions.map((a) => (
                <th scope="col" key={a} style={{ textAlign: 'start' }}>{t('action', { action: a })}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              // Sub-resource codes (finance.invoices, workflow.tasks) use underscores as ICU select keys.
              const moduleLabel = t('module', { module: row.module.replace(/\./g, '_') });
              return (
                <tr key={row.module}>
                  <th scope="row" style={{ textAlign: 'start' }}>{moduleLabel}</th>
                  <td>{row.actions.some((p) => p.branchScoped) ? t('branchScoped') : t('orgWide')}</td>
                  {actions.map((a) => {
                    const permission = row.actions.find((p) => actionOf(p.code) === a);
                    if (!permission) return <td key={a} />;
                    const id = `perm-${permission.code.replace(/\W/g, '-')}`;
                    return (
                      <td key={a}>
                        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                          <input
                            id={id}
                            type="checkbox"
                            checked={set.has(permission.code)}
                            disabled={disabled}
                            data-permission={permission.code}
                            aria-describedby={permission.isSensitive ? `${id}-sensitive` : undefined}
                            onChange={(e) => toggle(permission.code, e.target.checked)}
                          />
                          <label htmlFor={id}>
                            <span className="eemp-visually-hidden">{moduleLabel} </span>
                            {t('action', { action: a })}
                          </label>
                          {permission.isSensitive && (
                            <Badge tone="danger" id={`${id}-sensitive`}>{t('sensitive')}</Badge>
                          )}
                        </span>
                      </td>
                    );
                  })}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function RoleForm({
  initial, onSubmit, busy, error, onReload, isSystem, isEdit, readOnly, cancelHref,
}: {
  initial: RoleInput; onSubmit: (input: RoleInput) => void; busy: boolean; error: unknown; onReload?: () => void;
  /** The code is immutable once created (PUT /roles/{id} does not take it). */
  isSystem?: boolean; isEdit?: boolean; readOnly?: boolean; cancelHref: string;
}) {
  const t = useTranslations('roles');
  const common = useTranslations('common');
  const [v, setV] = useState(initial);
  const permissions = useApiQuery('permissions', (api) => api.permissions.list());
  const errors = useFieldErrors(error);
  return (
    <form
      onSubmit={(e: FormEvent) => {
        e.preventDefault();
        onSubmit({ ...v, code: v.code.trim(), name: v.name.trim() });
      }}
    >
      <ErrorAlert error={error} onReload={onReload} />
      {isSystem && <Alert tone="info" style={{ marginBlockEnd: 12 }}>{t('systemHint')}</Alert>}
      <fieldset disabled={readOnly} style={{ border: 0, padding: 0, margin: 0 }}>
        <FormGrid>
          <TextField label={t('code')} dir="ltr" required readOnly={isSystem || isEdit} value={v.code} error={errors.code} onChange={(e) => setV({ ...v, code: e.target.value })} />
          <TextField label={t('name')} required value={v.name} error={errors.name} onChange={(e) => setV({ ...v, name: e.target.value })} />
        </FormGrid>
        <h2 style={{ fontSize: '1.0625rem', marginBlock: '24px 8px' }}>{t('permissions')}</h2>
        {errors.permissions && <Alert tone="danger">{errors.permissions}</Alert>}
        {permissions.error ? (
          <LoadError error={permissions.error} onRetry={permissions.reload} />
        ) : !permissions.data ? (
          <Spinner label={common('loading')} />
        ) : (
          <PermissionMatrix permissions={permissions.data} selected={v.permissions} onChange={(codes) => setV({ ...v, permissions: codes })} disabled={readOnly} />
        )}
      </fieldset>
      {!readOnly && <FormActions cancelHref={cancelHref} busy={busy} />}
    </form>
  );
}

export function RoleCreateScreen() {
  const t = useTranslations('roles');
  const router = useRouter();
  const mutation = useApiMutation((api, input: RoleInput) => api.roles.create(input));
  return (
    <>
      <PageHeader before={<BackLink href="/roles" />} title={t('createTitle')} />
      <Card>
        <RoleForm
          initial={{ code: '', name: '', permissions: [] }}
          busy={mutation.busy}
          error={mutation.error}
          cancelHref="/roles"
          onSubmit={async (input) => {
            const result = await mutation.run(input);
            if (result.ok) router.push(`/roles/${result.value.id}?notice=created`);
          }}
        />
      </Card>
    </>
  );
}

export function RoleEditScreen({ id, notice }: { id: string; notice?: string }) {
  const t = useTranslations('roles');
  const common = useTranslations('common');
  const router = useRouter();
  const { can } = useSession();
  const canManage = can('roles.manage');
  const query = useApiQuery(`role|${id}`, (api) => api.roles.get(id));
  const [formKey, setFormKey] = useState(0);
  const [saved, setSaved] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const mutation = useApiMutation((api, role: Role, input: RoleInput) => api.roles.update(role.id, { name: input.name, permissions: input.permissions, rowVersion: role.rowVersion }));
  const remove = useApiMutation((api, roleId: string) => api.roles.remove(roleId));

  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const role = query.data;
  if (!role) return <Spinner label={common('loading')} />;
  return (
    <>
      <PageHeader
        before={<BackLink href="/roles" />}
        title={role.name}
        actions={
          <>
            <Badge tone={role.isSystem ? 'info' : 'neutral'}>{role.isSystem ? t('system') : t('custom')}</Badge>
            {canManage && !role.isSystem && <Button variant="danger" onClick={() => setConfirmDelete(true)}>{common('delete')}</Button>}
          </>
        }
      />
      <Notice notice={saved ? 'saved' : notice} />
      <Card>
        <RoleForm
          key={`${role.rowVersion}-${formKey}`}
          initial={{ code: role.code, name: role.name, permissions: role.permissions }}
          isSystem={role.isSystem}
          isEdit
          readOnly={!canManage}
          busy={mutation.busy}
          error={mutation.error}
          cancelHref="/roles"
          onReload={() => {
            mutation.reset();
            setFormKey((k) => k + 1);
            query.reload();
          }}
          onSubmit={async (input) => {
            setSaved(false);
            const result = await mutation.run(role, input);
            if (result.ok) {
              query.setData(result.value);
              setSaved(true);
            }
          }}
        />
      </Card>
      <ConfirmDialog
        open={confirmDelete}
        title={common('confirmDeleteTitle', { name: role.name })}
        description={common('confirmDeleteBody')}
        confirmLabel={common('delete')}
        cancelLabel={common('cancel')}
        busy={remove.busy}
        onCancel={() => { remove.reset(); setConfirmDelete(false); }}
        onConfirm={async () => {
          const result = await remove.run(role.id);
          if (result.ok) router.push('/roles?notice=deleted');
        }}
      >
        <ErrorAlert error={remove.error} />
      </ConfirmDialog>
    </>
  );
}
