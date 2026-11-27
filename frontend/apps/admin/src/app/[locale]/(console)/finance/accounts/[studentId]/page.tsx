import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { AccountDetailScreen } from '../../../../../../screens/finance/accounts';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.accounts');
  return { title: t('title') };
}

export default async function Page({ params }: { params: Promise<{ studentId: string }> }) {
  const { studentId } = await params;
  return <AccountDetailScreen key={studentId} studentId={studentId} />;
}
