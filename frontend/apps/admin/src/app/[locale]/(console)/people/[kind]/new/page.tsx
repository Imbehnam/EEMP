import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PersonCreateScreen } from '../../../../../../screens/people';
import { parseKind } from '../kind';

type Props = { params: Promise<{ kind: string }> };

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const t = await getTranslations('people');
  return { title: t('createTitle', { kind: (await params).kind }) };
}

export default async function Page({ params }: Props) {
  const kind = parseKind((await params).kind);
  return <PersonCreateScreen key={kind} kind={kind} />;
}
