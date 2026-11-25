import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { YearsListScreen } from '../../../../../screens/academic/catalog';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.years');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ notice?: string }> }) {
  return <YearsListScreen notice={(await searchParams).notice} />;
}
