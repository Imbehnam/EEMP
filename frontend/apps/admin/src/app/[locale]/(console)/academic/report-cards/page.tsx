import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ReportCardsScreen } from '../../../../../screens/academic/grades';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.reportCards');
  return { title: t('title') };
}

export default function Page() {
  return <ReportCardsScreen />;
}
