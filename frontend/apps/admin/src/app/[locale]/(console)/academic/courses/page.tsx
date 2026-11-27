import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { CoursesListScreen } from '../../../../../screens/academic/catalog';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.courses');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ notice?: string }> }) {
  return <CoursesListScreen notice={(await searchParams).notice} />;
}
