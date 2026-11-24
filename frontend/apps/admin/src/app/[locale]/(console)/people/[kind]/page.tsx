import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PeopleListScreen } from '../../../../../screens/people';
import { parseKind } from './kind';

type Props = { params: Promise<{ kind: string }>; searchParams: Promise<{ notice?: string }> };

export async function generateMetadata({ params }: Pick<Props, 'params'>): Promise<Metadata> {
  const t = await getTranslations('people');
  return { title: t('title', { kind: (await params).kind }) };
}

export default async function Page({ params, searchParams }: Props) {
  const kind = parseKind((await params).kind);
  return <PeopleListScreen key={kind} kind={kind} notice={(await searchParams).notice} />;
}
