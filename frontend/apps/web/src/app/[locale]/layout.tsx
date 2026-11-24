import '../globals.css';

import { LocaleDocument, resolveLocale } from '@eemp/app/server';
import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import type { ReactNode } from 'react';

type Props = { children: ReactNode; params: Promise<{ locale: string }> };

export async function generateMetadata({ params }: Pick<Props, 'params'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'meta' });
  return {
    title: { default: t('webTitle'), template: `%s · ${t('webTitle')}` },
    description: t('description'),
    robots: { index: false, follow: false },
  };
}

export default async function LocaleLayout({ children, params }: Props) {
  const locale = resolveLocale((await params).locale);
  return <LocaleDocument locale={locale}>{children}</LocaleDocument>;
}
