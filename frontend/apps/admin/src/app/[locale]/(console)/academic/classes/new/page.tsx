import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ClassCreateScreen } from '../../../../../../screens/academic/classes';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.classes');
  return { title: t('createTitle') };
}

export default function Page() {
  return <ClassCreateScreen />;
}
