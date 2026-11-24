import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { TenantEditScreen } from '../../../../../screens/tenants';

type Props = { params: Promise<{ id: string }>; searchParams: Promise<{ notice?: string }> };

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('tenants');
  return { title: t('editTitle') };
}

export default async function Page({ params, searchParams }: Props) {
  const [{ id }, { notice }] = await Promise.all([params, searchParams]);
  return <TenantEditScreen key={id} id={id} notice={notice} />;
}
