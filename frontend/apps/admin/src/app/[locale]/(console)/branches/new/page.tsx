import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { BranchCreateScreen } from '../../../../../screens/branches';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('branches');
  return { title: t('createTitle') };
}

export default function Page() {
  return <BranchCreateScreen />;
}
