import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { FinanceApprovalsScreen } from '../../../../../screens/finance/approvals';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.approvals');
  return { title: t('title') };
}

export default function Page() {
  return <FinanceApprovalsScreen />;
}
