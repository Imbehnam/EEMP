import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { MyStudentsScreen } from '../../../../screens/portal';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('portal');
  return { title: t('myStudentsTitle') };
}

export default function Page() {
  return <MyStudentsScreen />;
}
