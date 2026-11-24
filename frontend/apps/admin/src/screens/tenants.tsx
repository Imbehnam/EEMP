'use client';

import type { CreateTenantRequest, Tenant } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery } from '@eemp/app';
import { listTimeZones, localeMetadata, locales, toApiLocale, toSupportedLocale } from '@eemp/i18n';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link, useRouter } from '@eemp/i18n/navigation';
import { Badge, Button, Card, Checkbox, ConfirmDialog, Fieldset, FormGrid, PageHeader, SelectField, Spinner, TextField } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useMemo, useState, type FormEvent } from 'react';

import { BackLink, CreateButton, FormActions, Notice, PagedList, useFieldErrors } from '../components/common';

const localeOptions = () => locales.map((code) => ({ value: code, label: localeMetadata[code].nativeName, lang: code }));

export function TenantStatusBadge({ status }: { status: string }) {
  const t = useTranslations('tenants');
  return <Badge tone={status === 'active' ? 'success' : status === 'suspended' ? 'danger' : 'neutral'}>{t('statusLabel', { status })}</Badge>;
}

function useStatusToggle(onDone: (tenant?: Tenant) => void) {
  const [target, setTarget] = useState<Tenant | null>(null);
  const mutation = useApiMutation((api, tenant: Tenant) =>
    tenant.status === 'suspended' ? api.platform.tenants.activate(tenant.id) : api.platform.tenants.suspend(tenant.id),
  );
  return {
    target,
    open: setTarget,
    mutation,
    async confirm() {
      if (!target) return;
      const result = await mutation.run(target);
      if (result.ok) {
        setTarget(null);
        onDone(result.value || undefined);
      }
    },
  };
}

function StatusDialog({ toggle }: { toggle: ReturnType<typeof useStatusToggle> }) {
  const t = useTranslations('tenants');
  const common = useTranslations('common');
  const target = toggle.target;
  const activating = target?.status === 'suspended';
  return (
    <ConfirmDialog
      open={!!target}
      tone={activating ? 'primary' : 'danger'}
      title={target ? (activating ? t('activateTitle', { name: target.name }) : t('suspendTitle', { name: target.name })) : ''}
      description={activating ? t('activateBody') : t('suspendBody')}
      confirmLabel={activating ? t('activate') : t('suspend')}
      cancelLabel={common('cancel')}
      busy={toggle.mutation.busy}
      onConfirm={toggle.confirm}
      onCancel={() => {
        toggle.mutation.reset();
        toggle.open(null);
      }}
    >
      <ErrorAlert error={toggle.mutation.error} />
    </ConfirmDialog>
  );
}

export function TenantsListScreen({ notice }: { notice?: string }) {
  const t = useTranslations('tenants');
  const common = useTranslations('common');
  const format = useLocaleFormat();
  const [version, setVersion] = useState(0);
  const toggle = useStatusToggle(() => setVersion((v) => v + 1));
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={<CreateButton href="/tenants/new" label={t('new')} />} />
      <Notice notice={notice} />
      <PagedList<Tenant>
        queryKey={`tenants-${version}`}
        load={(api, q) => api.platform.tenants.list(q)}
        caption={t('title')}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        getRowKey={(row) => row.id}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (row) => <Link href={`/tenants/${row.id}`}>{row.name}</Link> },
          { key: 'slug', header: t('slug'), cell: (row) => <span className="eemp-ltr">{row.slug}</span> },
          { key: 'status', header: common('status'), cell: (row) => <TenantStatusBadge status={row.status} /> },
          { key: 'locale', header: t('defaultLocale'), cell: (row) => localeMetadata[toSupportedLocale(row.defaultLocale)].nativeName },
          { key: 'created', header: t('createdAt'), cell: (row) => format.instantDate(row.createdAt) },
          {
            key: 'actions',
            header: common('actions'),
            cell: (row) => (
              <Button size="sm" variant={row.status === 'suspended' ? 'secondary' : 'danger'} onClick={() => toggle.open(row)}>
                {row.status === 'suspended' ? t('activate') : t('suspend')}
              </Button>
            ),
          },
        ]}
      />
      <StatusDialog toggle={toggle} />
    </>
  );
}

interface TenantFormValues {
  slug: string;
  name: string;
  defaultLocale: string;
  supportedLocales: string[];
  timeZone: string;
  currency: string;
}

function TenantFields({
  values, onChange, errors, isCreate,
}: { values: TenantFormValues; onChange: (v: TenantFormValues) => void; errors: Record<string, string>; isCreate: boolean }) {
  const t = useTranslations('tenants');
  const common = useTranslations('common');
  const zones = useMemo(() => listTimeZones(), []);
  const set = <K extends keyof TenantFormValues>(key: K, value: TenantFormValues[K]) => onChange({ ...values, [key]: value });
  return (
    <FormGrid>
      <TextField
        label={t('slug')}
        hint={t('slugHint')}
        dir="ltr"
        required
        readOnly={!isCreate}
        disabled={!isCreate}
        value={values.slug}
        error={errors.slug}
        requiredMark={common('requiredMark')}
        onChange={(e) => set('slug', e.target.value.toLowerCase())}
      />
      <TextField label={t('name')} required value={values.name} error={errors.name} onChange={(e) => set('name', e.target.value)} />
      <SelectField
        label={t('defaultLocale')}
        required
        value={values.defaultLocale}
        error={errors.defaultLocale}
        options={localeOptions()}
        onChange={(e) => {
          const next = e.target.value;
          set('defaultLocale', next);
          if (!values.supportedLocales.includes(next)) onChange({ ...values, defaultLocale: next, supportedLocales: [...values.supportedLocales, next] });
        }}
      />
      <Fieldset>
        <legend>{t('supportedLocales')}</legend>
        <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
          {locales.map((code) => (
            <Checkbox
              key={code}
              label={<span lang={code}>{localeMetadata[code].nativeName}</span>}
              checked={values.supportedLocales.includes(code)}
              disabled={code === values.defaultLocale}
              onChange={(e) =>
                set('supportedLocales', e.target.checked ? [...values.supportedLocales, code] : values.supportedLocales.filter((l) => l !== code))
              }
            />
          ))}
        </div>
      </Fieldset>
      <SelectField
        label={t('timeZone')}
        dir="ltr"
        required
        value={values.timeZone}
        error={errors.timeZone}
        options={zones.map((z) => ({ value: z, label: z }))}
        onChange={(e) => set('timeZone', e.target.value)}
      />
      <TextField
        label={t('currency')}
        hint={t('currencyHint')}
        dir="ltr"
        required
        maxLength={3}
        value={values.currency}
        error={errors.currency}
        onChange={(e) => set('currency', e.target.value.toUpperCase())}
      />
    </FormGrid>
  );
}

/** Form values hold UI locale codes (`fa`); the API takes its own tags (`fa-IR`). */
const toApiLocales = (v: Pick<TenantFormValues, 'defaultLocale' | 'supportedLocales'>) => ({
  defaultLocale: toApiLocale(v.defaultLocale),
  supportedLocales: v.supportedLocales.map(toApiLocale),
});

export function TenantCreateScreen() {
  const t = useTranslations('tenants');
  const router = useRouter();
  const [values, setValues] = useState<TenantFormValues>({
    slug: '', name: '', defaultLocale: 'fa', supportedLocales: ['fa'], timeZone: 'Asia/Tehran', currency: 'IRR',
  });
  const [admin, setAdmin] = useState({ userName: '', displayName: '', email: '', phone: '', password: '' });
  const mutation = useApiMutation((api, body: CreateTenantRequest) => api.platform.tenants.create(body));
  const errors = useFieldErrors(mutation.error);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const result = await mutation.run({ ...values, ...toApiLocales(values), adminUser: admin });
    if (result.ok) router.push(`/tenants/${result.value.id}?notice=created`);
  }

  return (
    <>
      <PageHeader before={<BackLink href="/tenants" />} title={t('createTitle')} />
      <Card>
        <ErrorAlert error={mutation.error} />
        <form onSubmit={onSubmit}>
          <TenantFields values={values} onChange={setValues} errors={errors} isCreate />
          <Fieldset style={{ marginBlockStart: 20 }}>
            <legend>{t('adminSection')}</legend>
            <p style={{ marginBlock: '0 12px', color: 'var(--eemp-color-fg-muted)' }}>{t('adminSectionHint')}</p>
            <FormGrid>
              <TextField label={t('adminUserName')} dir="ltr" required autoComplete="off" value={admin.userName}
                error={errors['adminUser.userName']} onChange={(e) => setAdmin({ ...admin, userName: e.target.value })} />
              <TextField label={t('adminDisplayName')} required value={admin.displayName}
                error={errors['adminUser.displayName']} onChange={(e) => setAdmin({ ...admin, displayName: e.target.value })} />
              <TextField label={t('adminEmail')} type="email" dir="ltr" value={admin.email}
                error={errors['adminUser.email']} onChange={(e) => setAdmin({ ...admin, email: e.target.value })} />
              <TextField label={t('adminPhone')} type="tel" dir="ltr" value={admin.phone}
                error={errors['adminUser.phone']} onChange={(e) => setAdmin({ ...admin, phone: e.target.value })} />
              <TextField label={t('adminPassword')} type="password" dir="ltr" required autoComplete="new-password" value={admin.password}
                error={errors['adminUser.password']} onChange={(e) => setAdmin({ ...admin, password: e.target.value })} />
            </FormGrid>
          </Fieldset>
          <FormActions cancelHref="/tenants" busy={mutation.busy} />
        </form>
      </Card>
    </>
  );
}

export function TenantEditScreen({ id, notice }: { id: string; notice?: string }) {
  const t = useTranslations('tenants');
  const common = useTranslations('common');
  const format = useLocaleFormat();
  const query = useApiQuery(`tenant|${id}`, (api) => api.platform.tenants.get(id));
  const [values, setValues] = useState<TenantFormValues | null>(null);
  const [saved, setSaved] = useState(false);
  const mutation = useApiMutation((api, tenant: Tenant, v: TenantFormValues) =>
    api.platform.tenants.update(tenant.id, {
      name: v.name, ...toApiLocales(v), timeZone: v.timeZone, currency: v.currency, rowVersion: tenant.rowVersion,
    }),
  );
  const toggle = useStatusToggle((updated) => (updated ? query.setData(updated) : query.reload()));
  const errors = useFieldErrors(mutation.error);

  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const tenant = query.data;
  if (!tenant) return <Spinner label={common('loading')} />;
  const current: TenantFormValues = values ?? {
    slug: tenant.slug, name: tenant.name, defaultLocale: toSupportedLocale(tenant.defaultLocale),
    supportedLocales: tenant.supportedLocales.map((l) => toSupportedLocale(l)),
    timeZone: tenant.timeZone, currency: tenant.currency,
  };

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!tenant) return;
    setSaved(false);
    const result = await mutation.run(tenant, current);
    if (result.ok) {
      query.setData(result.value);
      setValues(null);
      setSaved(true);
    }
  }

  return (
    <>
      <PageHeader
        before={<BackLink href="/tenants" />}
        title={tenant.name}
        description={<>{t('createdAt')}: {format.dateTime(tenant.createdAt)}</>}
        actions={
          <>
            <TenantStatusBadge status={tenant.status} />
            <Button variant={tenant.status === 'suspended' ? 'secondary' : 'danger'} onClick={() => toggle.open(tenant)}>
              {tenant.status === 'suspended' ? t('activate') : t('suspend')}
            </Button>
          </>
        }
      />
      <Notice notice={saved ? 'saved' : notice} />
      <Card>
        <h2 style={{ marginBlockStart: 0, fontSize: '1.125rem' }}>{t('editTitle')}</h2>
        <ErrorAlert
          error={mutation.error}
          onReload={() => {
            mutation.reset();
            setValues(null);
            query.reload();
          }}
        />
        <form onSubmit={onSubmit}>
          <TenantFields values={current} onChange={setValues} errors={errors} isCreate={false} />
          <FormActions cancelHref="/tenants" busy={mutation.busy} />
        </form>
      </Card>
      <StatusDialog toggle={toggle} />
    </>
  );
}
