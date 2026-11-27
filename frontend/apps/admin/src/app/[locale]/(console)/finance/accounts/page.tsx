import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { AccountsScreen } from '../../../../../screens/finance/accounts';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('finance.accounts');
  return { title: t('title') };
}

export default function Page() {
  return <AccountsScreen />;
}
