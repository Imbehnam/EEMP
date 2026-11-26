import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PaymentResultScreen } from '../../../../../screens/finance';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.portal');
  return { title: t('resultTitle') };
}

type Props = { searchParams: Promise<{ paymentId?: string; invoiceId?: string }> };

/** Return page after the gateway (Finance:Payments:ReturnUrl). The status query parameter is ignored: the API verifies. */
export default async function Page({ searchParams }: Props) {
  const { paymentId, invoiceId } = await searchParams;
  return <PaymentResultScreen key={paymentId ?? ''} paymentId={paymentId} invoiceId={invoiceId} />;
}
