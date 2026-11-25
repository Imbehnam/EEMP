import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { SubjectsListScreen } from '../../../../../screens/academic/catalog';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.subjects');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ notice?: string }> }) {
  return <SubjectsListScreen notice={(await searchParams).notice} />;
}
