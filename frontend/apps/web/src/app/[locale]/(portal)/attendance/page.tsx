import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PortalAttendanceScreen } from '../../../../screens/academic';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.portal');
  return { title: t('attendanceTitle') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ student?: string }> }) {
  const { student } = await searchParams;
  return <PortalAttendanceScreen key={student ?? ''} student={student} />;
}
