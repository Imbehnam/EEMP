import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { SupportTicketScreen } from '../../../../../screens/support';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('support.ticket');
  return { title: t('title') };
}

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <SupportTicketScreen key={id} id={id} />;
}
