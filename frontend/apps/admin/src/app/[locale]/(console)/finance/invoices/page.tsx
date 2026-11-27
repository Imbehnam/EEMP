import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { InvoicesScreen } from '../../../../../screens/finance/invoices';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.invoices');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ notice?: string; studentId?: string }> }) {
  const { notice, studentId } = await searchParams;
  return <InvoicesScreen notice={notice} studentId={studentId} />;
}
