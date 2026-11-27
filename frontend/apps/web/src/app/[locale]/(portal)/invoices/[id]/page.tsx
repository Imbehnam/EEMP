import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PortalInvoiceScreen } from '../../../../../screens/finance';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.portal');
  return { title: t('invoices') };
}

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <PortalInvoiceScreen key={id} id={id} />;
}
