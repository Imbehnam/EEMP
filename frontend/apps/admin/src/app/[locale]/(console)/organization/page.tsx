import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { OrganizationScreen } from '../../../../screens/organization';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('organization');
  return { title: t('title') };
}

export default function Page() {
  return <OrganizationScreen />;
}
