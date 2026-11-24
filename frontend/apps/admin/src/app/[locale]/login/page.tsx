import { LoginForm } from '@eemp/app';
import { isMockMode, resolveLocale, safeNextPath } from '@eemp/app/server';
import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { APP_ID } from '../../api/auth/handlers';

type Props = {
  params: Promise<{ locale: string }>;
  searchParams: Promise<{ next?: string; reason?: string }>;
};

export async function generateMetadata({ params }: Pick<Props, 'params'>): Promise<Metadata> {
  const t = await getTranslations({ locale: (await params).locale, namespace: 'auth' });
  return { title: t('signInTitle') };
}

export default async function LoginPage({ params, searchParams }: Props) {
  const locale = resolveLocale((await params).locale);
  const query = await searchParams;
  const next = query.next ? safeNextPath(query.next, `/${locale}`) : undefined;
  const reason = query.reason === 'expired' || query.reason === 'signedOut' ? query.reason : undefined;
  return <LoginForm appId={APP_ID} variant="admin" next={next} reason={reason} mock={isMockMode(APP_ID)} />;
}
