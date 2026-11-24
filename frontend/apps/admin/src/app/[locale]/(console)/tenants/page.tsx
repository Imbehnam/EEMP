import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { TenantsListScreen } from '../../../../screens/tenants';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('tenants');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ notice?: string }> }) {
  return <TenantsListScreen notice={(await searchParams).notice} />;
}
