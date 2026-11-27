import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PortalReportCardScreen } from '../../../../../screens/academic';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.portal');
  return { title: t('reportCardTitle') };
}

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <PortalReportCardScreen key={id} id={id} />;
}
