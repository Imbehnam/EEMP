import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { TeacherClassScreen } from '../../../../../screens/academic';
import { isTeacherTab } from '../../../../../screens/academic-tabs';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.portal');
  return { title: t('classTitle') };
}

type Props = { params: Promise<{ id: string }>; searchParams: Promise<{ tab?: string }> };

export default async function Page({ params, searchParams }: Props) {
  const [{ id }, { tab }] = await Promise.all([params, searchParams]);
  return <TeacherClassScreen key={id} id={id} tab={isTeacherTab(tab) ? tab : 'attendance'} />;
}
