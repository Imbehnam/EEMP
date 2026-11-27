import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { CourseEditScreen } from '../../../../../../screens/academic/catalog';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.courses');
  return { title: t('editTitle') };
}

type Props = { params: Promise<{ id: string }>; searchParams: Promise<{ notice?: string }> };

export default async function Page({ params, searchParams }: Props) {
  const [{ id }, { notice }] = await Promise.all([params, searchParams]);
  return <CourseEditScreen key={id} id={id} notice={notice} />;
}
