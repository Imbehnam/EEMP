'use client';

import { calendarOf, formatDateTime, formatNumber, listTimeZones, localeMetadata, locales, toSupportedLocale } from '@eemp/i18n';
import { useErrorTranslator } from '@eemp/i18n/react';
import { Alert, Button, Card, DescriptionList, FormGrid, SelectField } from '@eemp/ui';
import { useLocale, useTimeZone, useTranslations } from 'next-intl';
import { useMemo, useState, type FormEvent } from 'react';

import { LOCALE_COOKIE, setPreferenceCookie, TIME_ZONE_COOKIE } from './cookies';
import { ErrorAlert, toApiErrorLike } from './ErrorAlert';
import { switchLocalePath } from './paths';
import { useSession } from './session';
import { useApiMutation } from './use-api';

/** Language & time zone preferences (PUT /auth/me/preferences). Calendar follows the language. */
export function PreferencesForm({ saved }: { saved?: boolean }) {
  const t = useTranslations('settings');
  const common = useTranslations('common');
  const errors = useErrorTranslator();
  const routeLocale = useLocale();
  const contextZone = useTimeZone();
  const { user } = useSession();
  const [locale, setLocale] = useState<string>(toSupportedLocale(user.locale, toSupportedLocale(routeLocale)));
  const [timeZone, setTimeZone] = useState<string>(user.timeZone || contextZone || 'Asia/Tehran');
  const zones = useMemo(() => {
    const all = listTimeZones();
    return all.includes(timeZone) ? all : [timeZone, ...all];
  }, [timeZone]);
  const mutation = useApiMutation((api, body: { locale: string; timeZone: string }) => api.auth.updatePreferences(body));
  const [now] = useState(() => new Date());

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const result = await mutation.run({ locale, timeZone });
    if (!result.ok) return;
    setPreferenceCookie(TIME_ZONE_COOKIE, timeZone);
    setPreferenceCookie(LOCALE_COOKIE, locale);
    const target = new URL(switchLocalePath(window.location.pathname, '', locale), window.location.origin);
    target.searchParams.set('saved', '1');
    window.location.assign(`${target.pathname}${target.search}`);
  }

  const fieldErrors = mutation.error ? errors.fields(toApiErrorLike(mutation.error).fieldErrors) : {};

  return (
    <Card>
      {saved && <Alert tone="success" role="status" style={{ marginBlockEnd: 16 }}>{t('saved')}</Alert>}
      <ErrorAlert error={mutation.error} />
      <form onSubmit={onSubmit} style={{ marginBlockStart: 8 }}>
        <FormGrid>
          <SelectField
            label={t('language')}
            name="locale"
            value={locale}
            onChange={(e) => setLocale(e.target.value)}
            error={fieldErrors.locale}
            options={locales.map((code) => ({ value: code, label: localeMetadata[code].nativeName, lang: code }))}
          />
          <SelectField
            label={t('timeZone')}
            hint={t('timeZoneHint')}
            name="timeZone"
            dir="ltr"
            value={timeZone}
            onChange={(e) => setTimeZone(e.target.value)}
            error={fieldErrors.timeZone}
            options={zones.map((zone) => ({ value: zone, label: zone }))}
          />
        </FormGrid>
        <section aria-labelledby="preview-heading" style={{ marginBlock: 20 }}>
          <h2 id="preview-heading" style={{ fontSize: '1rem', margin: '0 0 8px' }}>{t('preview')}</h2>
          <DescriptionList>
            <dt>{t('previewDateLabel')}</dt>
            <dd lang={locale} data-testid="preview-date" suppressHydrationWarning>{formatDateTime(now, { locale, timeZone }, { dateStyle: 'full', timeStyle: 'short' })}</dd>
            <dt>{t('previewNumberLabel')}</dt>
            <dd lang={locale}>{formatNumber(1234567.89, locale)}</dd>
          </DescriptionList>
          <p style={{ color: 'var(--eemp-color-fg-muted)', fontSize: '0.875rem' }}>
            {t('calendarNote', { calendar: calendarOf(locale) })}
          </p>
        </section>
        <Button type="submit" loading={mutation.busy}>{mutation.busy ? common('saving') : common('save')}</Button>
      </form>
    </Card>
  );
}
