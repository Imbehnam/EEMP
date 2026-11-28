import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PortalSupportScreen } from '../../../../screens/consultation';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('support.portal');
  return { title: t('title') };
}

export default function Page() {
  return <PortalSupportScreen />;
}
