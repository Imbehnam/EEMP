'use client';

import type { CurrentUser } from '@eemp/api-client';
import { toSupportedLocale } from '@eemp/i18n';
import { useErrorTranslator } from '@eemp/i18n/react';
import { Alert, Button, Card, Checkbox, FormGrid, TextField } from '@eemp/ui';
import { useLocale, useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';

import { postAuth } from './bff';
import { toApiErrorLike } from './ErrorAlert';
import { LocaleSwitcher } from './LocaleSwitcher';

export interface LoginFormProps {
  appId: string;
  /** Admin allows platform administrators (no institute code). */
  variant: 'admin' | 'web';
  /** Validated relative path to return to after sign-in. */
  next?: string;
  reason?: 'expired' | 'signedOut';
  mock?: boolean;
}

export function LoginForm({ appId, variant, next, reason, mock }: LoginFormProps) {
  const t = useTranslations('auth');
  const common = useTranslations('common');
  const errors = useErrorTranslator();
  const locale = useLocale();
  const [platform, setPlatform] = useState(false);
  const [tenantSlug, setTenantSlug] = useState('');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(undefined);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    try {
      const result = await postAuth<{ user: CurrentUser }>(
        appId,
        'login',
        { tenantSlug: platform ? null : tenantSlug.trim(), userName: userName.trim(), password },
        locale,
      );
      // Honour the user's saved language when there is no explicit return path.
      const target = next ?? `/${toSupportedLocale(result.user.locale, toSupportedLocale(locale))}`;
      window.location.assign(target);
    } catch (e) {
      setError(e);
      setBusy(false);
    }
  }

  const apiError = error ? toApiErrorLike(error) : undefined;
  const fieldErrors = apiError ? errors.fields(apiError.fieldErrors) : {};

  return (
    <main id="main-content" className="eemp-login">
      <div className="eemp-login-brand">
        <span className="eemp-login-mark" aria-hidden="true">{common('appName').charAt(0)}</span>
        <p className="eemp-login-app">{common('appName')}</p>
        <p className="eemp-login-tagline">{t('tagline')}</p>
      </div>
      <Card className="eemp-login-card">
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, marginBlockEnd: 20 }}>
          <span className="eemp-login-badge" aria-hidden="true">{common('appName')}</span>
          <LocaleSwitcher />
        </div>
        <h1 style={{ margin: 0, fontSize: '1.75rem' }}>{t('signInTitle')}</h1>
        <p style={{ marginBlock: '4px 24px', color: 'var(--eemp-color-fg-muted)' }}>
          {variant === 'admin' ? t('adminSubtitle') : t('portalSubtitle')}
        </p>
        {mock && <Alert tone="info" style={{ marginBlockEnd: 16 }}>{t('mockNotice')}</Alert>}
        {reason === 'expired' && !error && <Alert tone="warning" style={{ marginBlockEnd: 16 }}>{t('sessionExpired')}</Alert>}
        {reason === 'signedOut' && !error && <Alert tone="success" style={{ marginBlockEnd: 16 }}>{t('signedOut')}</Alert>}
        {apiError && (
          <Alert tone="danger" role="alert" style={{ marginBlockEnd: 16 }}>
            {errors.message(apiError)}
          </Alert>
        )}
        <form onSubmit={onSubmit} noValidate={false} aria-busy={busy || undefined}>
          <FormGrid>
            {variant === 'admin' && (
              <Checkbox
                label={t('platformAdmin')}
                hint={t('platformAdminHint')}
                checked={platform}
                onChange={(e) => setPlatform(e.target.checked)}
              />
            )}
            {!platform && (
              <TextField
                label={t('tenantSlug')}
                hint={t('tenantSlugHint')}
                name="tenantSlug"
                autoComplete="organization"
                dir="ltr"
                required
                value={tenantSlug}
                error={fieldErrors.tenantSlug}
                onChange={(e) => setTenantSlug(e.target.value)}
              />
            )}
            <TextField
              label={t('userName')}
              name="userName"
              autoComplete="username"
              dir="ltr"
              required
              value={userName}
              error={fieldErrors.userName}
              onChange={(e) => setUserName(e.target.value)}
            />
            <TextField
              label={t('password')}
              name="password"
              type="password"
              autoComplete="current-password"
              dir="ltr"
              required
              value={password}
              error={fieldErrors.password}
              onChange={(e) => setPassword(e.target.value)}
            />
            <Button type="submit" size="lg" loading={busy} fullWidth>
              {t('submit')}
            </Button>
          </FormGrid>
        </form>
      </Card>
    </main>
  );
}
