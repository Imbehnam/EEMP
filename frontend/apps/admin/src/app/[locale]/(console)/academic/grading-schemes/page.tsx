import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { SchemesListScreen } from '../../../../../screens/academic/catalog';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.schemes');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ notice?: string }> }) {
  return <SchemesListScreen notice={(await searchParams).notice} />;
}
