import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { FeesScreen } from '../../../../../screens/finance/setup';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.fees');
  return { title: t('title') };
}

export default function Page() {
  return <FeesScreen />;
}
