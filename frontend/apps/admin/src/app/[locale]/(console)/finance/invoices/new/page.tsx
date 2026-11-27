import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { InvoiceCreateScreen } from '../../../../../../screens/finance/invoices';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.invoices');
  return { title: t('createTitle') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ studentId?: string }> }) {
  const { studentId } = await searchParams;
  return <InvoiceCreateScreen studentId={studentId} />;
}
