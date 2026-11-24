import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { RoleCreateScreen } from '../../../../../screens/roles';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('roles');
  return { title: t('createTitle') };
}

export default function Page() {
  return <RoleCreateScreen />;
}
