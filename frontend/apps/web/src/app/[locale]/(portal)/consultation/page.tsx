import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { BookConsultationScreen } from '../../../../screens/consultation';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('consultation.portal');
  return { title: t('bookTitle') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ student?: string }> }) {
  const { student } = await searchParams;
  return <BookConsultationScreen key={student ?? ''} student={student} />;
}
