import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ConsultantsScreen } from '../../../../../screens/consultation';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('consultation.consultants');
  return { title: t('title') };
}

export default function Page() {
  return <ConsultantsScreen />;
}
