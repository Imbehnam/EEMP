import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { WorkflowInstanceScreen } from '../../../../../../screens/workflow';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('workflow.instance');
  return { title: t('title') };
}

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <WorkflowInstanceScreen key={id} id={id} />;
}
