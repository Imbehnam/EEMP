import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PaymentDetailScreen } from '../../../../../../screens/finance/payments';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.payments');
  return { title: t('detailTitle') };
}

type Props = { params: Promise<{ id: string }>; searchParams: Promise<{ notice?: string }> };

export default async function Page({ params, searchParams }: Props) {
  const [{ id }, { notice }] = await Promise.all([params, searchParams]);
  return <PaymentDetailScreen key={id} id={id} notice={notice} />;
}
