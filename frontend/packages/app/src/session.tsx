'use client';

import { createBrowserEndpoints, type CurrentUser, type Endpoints } from '@eemp/api-client';
import { useLocale } from 'next-intl';
import { createContext, useContext, useMemo, type ReactNode } from 'react';

export interface SessionValue {
  appId: string;
  user: CurrentUser;
  /** Typed endpoints bound to this app's BFF (/api/bff). */
  api: Endpoints;
  /** UX hint only: the backend authorizes every call. True if any assignment grants the permission. */
  can: (permission: string) => boolean;
}

const SessionContext = createContext<SessionValue | null>(null);

export function loginUrl(locale: string, next?: string, reason?: string) {
  const params = new URLSearchParams();
  if (next) params.set('next', next);
  if (reason) params.set('reason', reason);
  const qs = params.toString();
  return `/${locale}/login${qs ? `?${qs}` : ''}`;
}

export function SessionProvider({ appId, user, children }: { appId: string; user: CurrentUser; children: ReactNode }) {
  const locale = useLocale();
  const value = useMemo<SessionValue>(() => {
    const permissions = new Set(user.permissions);
    return {
      appId,
      user,
      api: createBrowserEndpoints({
        appId,
        locale,
        onSessionExpired: () => {
          const next = `${window.location.pathname}${window.location.search}`;
          window.location.assign(loginUrl(locale, next, 'expired'));
        },
      }),
      can: (permission) => permissions.has(permission),
    };
  }, [appId, user, locale]);
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionValue {
  const value = useContext(SessionContext);
  if (!value) throw new Error('useSession must be used inside <SessionProvider>');
  return value;
}
