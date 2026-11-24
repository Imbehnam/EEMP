import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { ProfileScreen } from '../../../../screens/portal';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('portal');
  return { title: t('profileTitle') };
}

export default function Page() {
  return <ProfileScreen />;
}
