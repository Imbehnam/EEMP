import { AppFrame, SessionProvider } from '@eemp/app';
import { getThemePreference, requireUser, resolveLocale } from '@eemp/app/server';
import { getTranslations } from 'next-intl/server';
import type { ReactNode } from 'react';

import { APP_ID } from '../../api/auth/handlers';
import { adminNavigation } from '../../../navigation';

type Props = { children: ReactNode; params: Promise<{ locale: string }> };

export default async function ConsoleLayout({ children, params }: Props) {
  const locale = resolveLocale((await params).locale);
  const [user, theme, t] = await Promise.all([requireUser(APP_ID, locale), getThemePreference(), getTranslations('common')]);
  const shell = await getTranslations('shell');
  return (
    <SessionProvider appId={APP_ID} user={user}>
      <AppFrame
        brand={`${t('appName')} · ${t('adminConsole')}`}
        context={user.isPlatformAdmin ? shell('platformContext') : (user.tenantName ?? undefined)}
        sections={adminNavigation(user)}
        theme={theme}
      >
        {children}
      </AppFrame>
    </SessionProvider>
  );
}
