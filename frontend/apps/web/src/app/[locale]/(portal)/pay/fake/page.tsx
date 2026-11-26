import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { FakeGatewayScreen } from '../../../../../screens/finance';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.portal');
  return { title: t('gatewayTitle') };
}

type Props = { searchParams: Promise<{ paymentId?: string; authority?: string; invoiceId?: string }> };

/** Development fake gateway page (the API's fake gateway is enabled only in Development/Testing). */
export default async function Page({ searchParams }: Props) {
  const { paymentId, authority, invoiceId } = await searchParams;
  return <FakeGatewayScreen paymentId={paymentId} authority={authority} invoiceId={invoiceId} />;
}
