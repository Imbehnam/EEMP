import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { PortalTicketScreen } from '../../../../../screens/consultation';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('support.portal');
  return { title: t('threadTitle') };
}

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <PortalTicketScreen key={id} id={id} />;
}
