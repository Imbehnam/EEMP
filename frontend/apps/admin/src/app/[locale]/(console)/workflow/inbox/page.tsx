import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { WorkflowInboxScreen } from '../../../../../screens/workflow';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('workflow.inbox');
  return { title: t('title') };
}

export default function Page() {
  return <WorkflowInboxScreen />;
}
