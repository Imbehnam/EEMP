import type { PortalKind } from '@eemp/api-client';
import { AppFrame, SessionProvider } from '@eemp/app';
import { getServerApi, getThemePreference, requireUser, resolveLocale } from '@eemp/app/server';
import { getTranslations } from 'next-intl/server';
import type { ReactNode } from 'react';

import { portalNavigation } from '../../../navigation';
import { APP_ID } from '../../api/auth/handlers';
import { PortalKindProvider } from './portal-kind';

type Props = { children: ReactNode; params: Promise<{ locale: string }> };

/** Resolves which record the user is linked to (drives the "My students" entry). Null when unlinked. */
async function portalKind(locale: string): Promise<PortalKind | null> {
  try {
    const api = await getServerApi(APP_ID, locale);
    return api ? (await api.portal.profile()).kind : null;
  } catch {
    return null;
  }
}

export default async function PortalLayout({ children, params }: Props) {
  const locale = resolveLocale((await params).locale);
  const user = await requireUser(APP_ID, locale);
  const [kind, theme, t] = await Promise.all([portalKind(locale), getThemePreference(), getTranslations('common')]);
  return (
    <SessionProvider appId={APP_ID} user={user}>
      <PortalKindProvider kind={kind}>
        <AppFrame
          brand={`${t('appName')} · ${t('clientPortal')}`}
          context={user.tenantName ?? undefined}
          sections={portalNavigation(kind)}
          theme={theme}
        >
          {children}
        </AppFrame>
      </PortalKindProvider>
    </SessionProvider>
  );
}
