import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PortalSettingsScreen } from '../../../../screens/portal';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('settings');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ saved?: string }> }) {
  return <PortalSettingsScreen saved={(await searchParams).saved === '1'} />;
}
