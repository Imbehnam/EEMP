import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { Home } from './home';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('portal');
  return { title: t('homeTitle') };
}

export default function Page() {
  return <Home />;
}
