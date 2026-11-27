import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ConsultationBookingScreen } from '../../../../../../screens/consultation';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('consultation.booking');
  return { title: t('title') };
}

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <ConsultationBookingScreen key={id} id={id} />;
}
