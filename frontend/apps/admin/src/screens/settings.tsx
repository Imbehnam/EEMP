'use client';

import { PreferencesForm } from '@eemp/app';
import { PageHeader } from '@eemp/ui';
import { useTranslations } from 'next-intl';

export function SettingsScreen({ saved }: { saved?: boolean }) {
  const t = useTranslations('settings');
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      <PreferencesForm saved={saved} />
    </>
  );
}
