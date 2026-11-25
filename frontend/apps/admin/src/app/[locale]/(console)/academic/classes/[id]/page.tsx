import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ClassDetailScreen, CLASS_TABS, type ClassTab } from '../../../../../../screens/academic/classes';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.classes');
  return { title: t('detailTitle') };
}

type Props = { params: Promise<{ id: string }>; searchParams: Promise<{ notice?: string; tab?: string }> };

export default async function Page({ params, searchParams }: Props) {
  const [{ id }, { notice, tab }] = await Promise.all([params, searchParams]);
  const active = (CLASS_TABS as readonly string[]).includes(tab ?? '') ? (tab as ClassTab) : 'overview';
  return <ClassDetailScreen key={id} id={id} tab={active} notice={notice} />;
}
