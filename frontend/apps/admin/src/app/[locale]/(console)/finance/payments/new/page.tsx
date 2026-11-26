import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { RecordPaymentScreen } from '../../../../../../screens/finance/payments';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.payments');
  return { title: t('recordTitle') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ studentId?: string; invoiceId?: string }> }) {
  const { studentId, invoiceId } = await searchParams;
  return <RecordPaymentScreen key={`${studentId ?? ''}|${invoiceId ?? ''}`} studentId={studentId} invoiceId={invoiceId} />;
}
