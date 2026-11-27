'use client';

import { EmotionRegistry, ToastProvider, type Dir } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import type { ReactNode } from 'react';

/** Client providers shared by every page (Emotion SSR registry with RTL-aware cache + theme, toasts). */
export function AppProviders({ dir, nonce, children }: { dir: Dir; nonce?: string; children: ReactNode }) {
  const t = useTranslations('common');
  return (
    <EmotionRegistry key={dir} dir={dir} nonce={nonce}>
      <ToastProvider closeLabel={t('close')}>{children}</ToastProvider>
    </EmotionRegistry>
  );
}
