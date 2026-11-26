import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { WorkflowRequestsScreen } from '../../../../../screens/workflow';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('workflow.requests');
  return { title: t('title') };
}

export default function Page() {
  return <WorkflowRequestsScreen />;
}
