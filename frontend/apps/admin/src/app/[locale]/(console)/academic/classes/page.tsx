import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ClassesListScreen } from '../../../../../screens/academic/classes';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.classes');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ notice?: string }> }) {
  return <ClassesListScreen notice={(await searchParams).notice} />;
}
