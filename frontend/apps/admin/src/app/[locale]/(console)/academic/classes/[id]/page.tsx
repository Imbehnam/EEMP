import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ClassDetailScreen } from '../../../../../../screens/academic/classes';
import { isClassTab } from '../../../../../../screens/academic/tabs';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.classes');
  return { title: t('detailTitle') };
}

type Props = { params: Promise<{ id: string }>; searchParams: Promise<{ notice?: string; tab?: string }> };

export default async function Page({ params, searchParams }: Props) {
  const [{ id }, { notice, tab }] = await Promise.all([params, searchParams]);
  return <ClassDetailScreen key={id} id={id} tab={isClassTab(tab) ? tab : 'overview'} notice={notice} />;
}
