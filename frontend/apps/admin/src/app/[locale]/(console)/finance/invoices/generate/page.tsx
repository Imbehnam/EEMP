import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { GenerateInvoicesScreen } from '../../../../../../screens/finance/invoices';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.generate');
  return { title: t('title') };
}

export default function Page() {
  return <GenerateInvoicesScreen />;
}
