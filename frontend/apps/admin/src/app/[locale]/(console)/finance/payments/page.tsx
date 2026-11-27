import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PaymentsScreen } from '../../../../../screens/finance/payments';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.payments');
  return { title: t('title') };
}

export default function Page() {
  return <PaymentsScreen />;
}
