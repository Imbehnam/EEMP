import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { SubjectCreateScreen } from '../../../../../../screens/academic/catalog';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.subjects');
  return { title: t('createTitle') };
}

export default function Page() {
  return <SubjectCreateScreen />;
}
