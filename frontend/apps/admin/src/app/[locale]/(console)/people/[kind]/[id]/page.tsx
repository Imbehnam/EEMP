import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PersonEditScreen } from '../../../../../../screens/people';
import { parseKind } from '../kind';

type Props = { params: Promise<{ kind: string; id: string }>; searchParams: Promise<{ notice?: string }> };

export async function generateMetadata({ params }: Pick<Props, 'params'>): Promise<Metadata> {
  const t = await getTranslations('people');
  return { title: t('editTitle', { kind: (await params).kind }) };
}

export default async function Page({ params, searchParams }: Props) {
  const [{ kind: rawKind, id }, { notice }] = await Promise.all([params, searchParams]);
  const kind = parseKind(rawKind);
  return <PersonEditScreen key={`${kind}-${id}`} kind={kind} id={id} notice={notice} />;
}
