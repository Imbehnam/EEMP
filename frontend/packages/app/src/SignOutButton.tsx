'use client';

import { Button } from '@eemp/ui';
import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';

import { postAuth } from './bff';
import { loginUrl, useSession } from './session';

export function SignOutButton() {
  const t = useTranslations('common');
  const locale = useLocale();
  const { appId } = useSession();
  const [busy, setBusy] = useState(false);
  return (
    <Button
      variant="secondary"
      size="sm"
      loading={busy}
      onClick={async () => {
        setBusy(true);
        try {
          await postAuth(appId, 'logout');
        } catch {
          // Cookies are cleared server-side even if revocation fails; continue to the login page.
        }
        window.location.assign(loginUrl(locale, undefined, 'signedOut'));
      }}
    >
      {t('signOut')}
    </Button>
  );
}
