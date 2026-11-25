import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ReportCardDetailScreen } from '../../../../../../screens/academic/grades';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.reportCards');
  return { title: t('pageTitle') };
}

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <ReportCardDetailScreen key={id} id={id} />;
}
