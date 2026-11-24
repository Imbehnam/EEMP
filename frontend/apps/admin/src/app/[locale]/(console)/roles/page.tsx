import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { RolesListScreen } from '../../../../screens/roles';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('roles');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ notice?: string }> }) {
  return <RolesListScreen notice={(await searchParams).notice} />;
}
