import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { FinanceReportsScreen } from '../../../../../screens/finance/reports';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.reports');
  return { title: t('title') };
}

export default function Page() {
  return <FinanceReportsScreen />;
}
