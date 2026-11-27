import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';

import { TeacherAssessmentScreen } from '../../../../../../../screens/academic';

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('academic.portal');
  return { title: t('assessmentTitle') };
}

type Props = { params: Promise<{ id: string; assessmentId: string }> };

export default async function Page({ params }: Props) {
  const { id, assessmentId } = await params;
  return <TeacherAssessmentScreen key={assessmentId} classId={id} assessmentId={assessmentId} />;
}
