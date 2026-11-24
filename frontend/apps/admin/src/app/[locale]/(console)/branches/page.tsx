import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { BranchesListScreen } from '../../../../screens/branches';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('branches');
  return { title: t('title') };
}

export default async function Page({ searchParams }: { searchParams: Promise<{ notice?: string }> }) {
  return <BranchesListScreen notice={(await searchParams).notice} />;
}
