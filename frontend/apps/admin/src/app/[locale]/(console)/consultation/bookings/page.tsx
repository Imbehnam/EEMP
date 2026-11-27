import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ConsultationBookingsScreen } from '../../../../../screens/consultation';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('consultation.bookings');
  return { title: t('title') };
}

export default function Page() {
  return <ConsultationBookingsScreen />;
}
