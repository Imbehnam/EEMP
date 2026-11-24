'use client';

import { Button, StatePanel } from '@eemp/ui';
import { useTranslations } from 'next-intl';

export default function ConsoleError({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  const t = useTranslations('states');
  const common = useTranslations('common');
  return (
    <StatePanel
      role="alert"
      title={t('errorTitle')}
      description={t('errorBody')}
      action={<Button variant="secondary" onClick={reset}>{common('retry')}</Button>}
    />
  );
}
