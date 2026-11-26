'use client';

import type { Branch, BranchInput } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { Link, useRouter } from '@eemp/i18n/navigation';
import { Button, Card, Checkbox, ConfirmDialog, FormGrid, PageHeader, Spinner, TextAreaField, TextField } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';

import { ActiveBadge, BackLink, CreateButton, FormActions, Notice, nullIfEmpty, PagedList, useFieldErrors } from '../components/common';

type BranchValues = { code: string; name: string; address: string; phone: string; isActive: boolean };
const empty: BranchValues = { code: '', name: '', address: '', phone: '', isActive: true };
const toInput = (v: BranchValues): BranchInput => ({
  code: v.code.trim(), name: v.name.trim(), address: nullIfEmpty(v.address), phone: nullIfEmpty(v.phone), isActive: v.isActive,
});

export function BranchesListScreen({ notice }: { notice?: string }) {
  const t = useTranslations('branches');
  const common = useTranslations('common');
  const { can } = useSession();
  return (
    <>
      <PageHeader
        title={t('title')}
        description={t('intro')}
        actions={can('branches.manage') && <CreateButton href="/branches/new" label={t('new')} />}
      />
      <Notice notice={notice} />
      <PagedList<Branch>
        queryKey="branches"
        load={(api, q) => api.branches.list(q)}
        caption={t('title')}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        emptyAction={can('branches.manage') && <CreateButton href="/branches/new" label={t('new')} />}
        getRowKey={(b) => b.id}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (b) => <Link href={`/branches/${b.id}`}>{b.name}</Link> },
          { key: 'code', header: t('code'), cell: (b) => <span className="eemp-ltr">{b.code}</span> },
          { key: 'phone', header: t('phone'), cell: (b) => (b.phone ? <span className="eemp-ltr">{b.phone}</span> : common('none')) },
          { key: 'status', header: common('status'), cell: (b) => <ActiveBadge active={b.isActive} /> },
        ]}
      />
    </>
  );
}

function BranchFields({ v, set, errors, disabled }: { v: BranchValues; set: (v: BranchValues) => void; errors: Record<string, string>; disabled?: boolean }) {
  const t = useTranslations('branches');
  return (
    <fieldset disabled={disabled} style={{ border: 0, padding: 0, margin: 0 }}>
      <FormGrid>
        <TextField label={t('code')} dir="ltr" required value={v.code} error={errors.code} onChange={(e) => set({ ...v, code: e.target.value })} />
        <TextField label={t('name')} required value={v.name} error={errors.name} onChange={(e) => set({ ...v, name: e.target.value })} />
        <TextField label={t('phone')} type="tel" dir="ltr" value={v.phone} error={errors.phone} onChange={(e) => set({ ...v, phone: e.target.value })} />
        <TextAreaField label={t('address')} rows={2} value={v.address} error={errors.address} onChange={(e) => set({ ...v, address: e.target.value })} />
        <Checkbox label={t('isActive')} checked={v.isActive} onChange={(e) => set({ ...v, isActive: e.target.checked })} />
      </FormGrid>
    </fieldset>
  );
}

export function BranchCreateScreen() {
  const t = useTranslations('branches');
  const router = useRouter();
  const [v, setV] = useState(empty);
  const mutation = useApiMutation((api, input: BranchInput) => api.branches.create(input));
  const errors = useFieldErrors(mutation.error);
  return (
    <>
      <PageHeader before={<BackLink href="/branches" />} title={t('createTitle')} />
      <Card>
        <ErrorAlert error={mutation.error} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            const result = await mutation.run(toInput(v));
            if (result.ok) router.push(`/branches/${result.value.id}?notice=created`);
          }}
        >
          <BranchFields v={v} set={setV} errors={errors} />
          <FormActions cancelHref="/branches" busy={mutation.busy} />
        </form>
      </Card>
    </>
  );
}

export function BranchEditScreen({ id, notice }: { id: string; notice?: string }) {
  const common = useTranslations('common');
  const router = useRouter();
  const { can } = useSession();
  const canManage = can('branches.manage');
  const query = useApiQuery(`branch|${id}`, (api) => api.branches.get(id));
  const [values, setValues] = useState<BranchValues | null>(null);
  const [saved, setSaved] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const mutation = useApiMutation((api, b: Branch, v: BranchValues) => api.branches.update(b.id, { ...toInput(v), rowVersion: b.rowVersion }));
  const remove = useApiMutation((api, branchId: string) => api.branches.remove(branchId));
  const errors = useFieldErrors(mutation.error);

  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const branch = query.data;
  if (!branch) return <Spinner label={common('loading')} />;
  const v = values ?? { code: branch.code, name: branch.name, address: branch.address ?? '', phone: branch.phone ?? '', isActive: branch.isActive };

  return (
    <>
      <PageHeader
        before={<BackLink href="/branches" />}
        title={branch.name}
        actions={canManage && <Button variant="danger" onClick={() => setConfirmDelete(true)}>{common('delete')}</Button>}
      />
      <Notice notice={saved ? 'saved' : notice} />
      <Card>
        <ErrorAlert error={mutation.error} onReload={() => { mutation.reset(); setValues(null); query.reload(); }} />
        <form
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            setSaved(false);
            const result = await mutation.run(branch, v);
            if (result.ok) {
              query.setData(result.value);
              setValues(null);
              setSaved(true);
            }
          }}
        >
          <BranchFields v={v} set={setValues} errors={errors} disabled={!canManage} />
          {canManage && <FormActions cancelHref="/branches" busy={mutation.busy} />}
        </form>
      </Card>
      <ConfirmDialog
        open={confirmDelete}
        title={common('confirmDeleteTitle', { name: branch.name })}
        description={common('confirmDeleteBody')}
        confirmLabel={common('delete')}
        cancelLabel={common('cancel')}
        busy={remove.busy}
        onCancel={() => { remove.reset(); setConfirmDelete(false); }}
        onConfirm={async () => {
          const result = await remove.run(branch.id);
          if (result.ok) router.push('/branches?notice=deleted');
        }}
      >
        <ErrorAlert error={remove.error} />
      </ConfirmDialog>
    </>
  );
}
