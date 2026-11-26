import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { WorkflowDefinitionsScreen } from '../../../../../screens/workflow';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('workflow.definitions');
  return { title: t('title') };
}

export default function Page() {
  return <WorkflowDefinitionsScreen />;
}
