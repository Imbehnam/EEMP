import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { YearCreateScreen } from '../../../../../../screens/academic/catalog';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.years');
  return { title: t('createTitle') };
}

export default function Page() {
  return <YearCreateScreen />;
}
