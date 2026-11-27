import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { MyConsultationsScreen } from '../../../../../screens/consultation';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('consultation.portal');
  return { title: t('myTitle') };
}

export default function Page() {
  return <MyConsultationsScreen />;
}
