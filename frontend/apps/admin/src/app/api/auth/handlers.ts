import { createAuthHandlers } from '@eemp/api-client/server';
import type { CurrentUser } from '@eemp/api-client';

/** Admin console: platform administrators and tenant users holding at least one permission. */
const ACCEPT = (user: CurrentUser) => user.isPlatformAdmin || user.permissions.length > 0;

export const APP_ID = 'admin';

export const authHandlers = createAuthHandlers({
  appId: APP_ID,
  loginPath: (next) => {
    const locale = /^\/(fa|en|tr|ar)(\/|$)/.exec(next)?.[1] ?? 'fa';
    return `/${locale}/login?next=${encodeURIComponent(next)}&reason=expired`;
  },
  acceptUser: (user: CurrentUser) => ACCEPT(user),
});
