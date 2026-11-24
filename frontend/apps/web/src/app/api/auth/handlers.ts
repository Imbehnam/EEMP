import { createAuthHandlers } from '@eemp/api-client/server';
import type { CurrentUser } from '@eemp/api-client';

/** Client portal: tenant members only (platform operators use the admin console). */
const ACCEPT = (user: CurrentUser) => !user.isPlatformAdmin;

export const APP_ID = 'web';

export const authHandlers = createAuthHandlers({
  appId: APP_ID,
  loginPath: (next) => {
    const locale = /^\/(fa|en|tr|ar)(\/|$)/.exec(next)?.[1] ?? 'fa';
    return `/${locale}/login?next=${encodeURIComponent(next)}&reason=expired`;
  },
  acceptUser: (user: CurrentUser) => ACCEPT(user),
});
