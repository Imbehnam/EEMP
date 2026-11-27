import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { InvoiceDetailScreen } from '../../../../../../screens/finance/invoices';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.invoices');
  return { title: t('title') };
}

type Props = { params: Promise<{ id: string }>; searchParams: Promise<{ notice?: string }> };

export default async function Page({ params, searchParams }: Props) {
  const [{ id }, { notice }] = await Promise.all([params, searchParams]);
  return <InvoiceDetailScreen key={id} id={id} notice={notice} />;
}
