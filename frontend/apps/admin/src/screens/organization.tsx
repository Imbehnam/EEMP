'use client';

import type { Organization } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { listTimeZones, localeMetadata, locales, toApiLocale, toSupportedLocale } from '@eemp/i18n';
import { Button, Card, FormGrid, PageHeader, SelectField, Spinner, TextAreaField, TextField } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useMemo, useState, type FormEvent } from 'react';

import { Notice, nullIfEmpty, useFieldErrors } from '../components/common';

type OrgValues = Record<'name' | 'legalName' | 'phone' | 'email' | 'address' | 'defaultLocale' | 'timeZone', string>;

const toValues = (o: Organization): OrgValues => ({
  name: o.name, legalName: o.legalName ?? '', phone: o.phone ?? '', email: o.email ?? '', address: o.address ?? '',
  defaultLocale: toSupportedLocale(o.defaultLocale), timeZone: o.timeZone,
});

export function OrganizationScreen() {
  const t = useTranslations('organization');
  const common = useTranslations('common');
  const { can } = useSession();
  const canManage = can('organization.manage');
  const zones = useMemo(() => listTimeZones(), []);
  const query = useApiQuery('organization', (api) => api.organization.get());
  const [values, setValues] = useState<OrgValues | null>(null);
  const [saved, setSaved] = useState(false);
  const mutation = useApiMutation((api, org: Organization, v: OrgValues) =>
    api.organization.update({
      name: v.name.trim(), legalName: nullIfEmpty(v.legalName), phone: nullIfEmpty(v.phone), email: nullIfEmpty(v.email),
      address: nullIfEmpty(v.address), defaultLocale: toApiLocale(v.defaultLocale), timeZone: v.timeZone, rowVersion: org.rowVersion,
    }),
  );
  const errors = useFieldErrors(mutation.error);

  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const org = query.data;
  if (!org) return <Spinner label={common('loading')} />;
  const v = values ?? toValues(org);
  const set = (key: keyof OrgValues, value: string) => setValues({ ...v, [key]: value });

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!org) return;
    setSaved(false);
    const result = await mutation.run(org, v);
    if (result.ok) {
      query.setData(result.value);
      setValues(null);
      setSaved(true);
    }
  }

  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      <Notice notice={saved ? 'saved' : undefined} />
      <Card>
        <ErrorAlert
          error={mutation.error}
          onReload={() => {
            mutation.reset();
            setValues(null);
            query.reload();
          }}
        />
        <form onSubmit={onSubmit}>
          <fieldset disabled={!canManage} style={{ border: 0, padding: 0, margin: 0 }}>
            <FormGrid>
              <TextField label={t('name')} required value={v.name} error={errors.name} onChange={(e) => set('name', e.target.value)} />
              <TextField label={t('legalName')} value={v.legalName} error={errors.legalName} onChange={(e) => set('legalName', e.target.value)} />
              <TextField label={t('phone')} type="tel" dir="ltr" value={v.phone} error={errors.phone} onChange={(e) => set('phone', e.target.value)} />
              <TextField label={t('email')} type="email" dir="ltr" value={v.email} error={errors.email} onChange={(e) => set('email', e.target.value)} />
              <TextAreaField label={t('address')} rows={2} value={v.address} error={errors.address} onChange={(e) => set('address', e.target.value)} />
              <SelectField
                label={t('defaultLocale')}
                value={v.defaultLocale}
                error={errors.defaultLocale}
                options={locales.map((code) => ({ value: code, label: localeMetadata[code].nativeName, lang: code }))}
                onChange={(e) => set('defaultLocale', e.target.value)}
              />
              <SelectField
                label={t('timeZone')}
                dir="ltr"
                value={v.timeZone}
                error={errors.timeZone}
                options={(zones.includes(v.timeZone) ? zones : [v.timeZone, ...zones]).map((z) => ({ value: z, label: z }))}
                onChange={(e) => set('timeZone', e.target.value)}
              />
            </FormGrid>
          </fieldset>
          {canManage && (
            <div style={{ marginBlockStart: 20 }}>
              <Button type="submit" loading={mutation.busy}>{mutation.busy ? common('saving') : common('save')}</Button>
            </div>
          )}
        </form>
      </Card>
    </>
  );
}
