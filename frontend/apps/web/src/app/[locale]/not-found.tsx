import { StatePanel } from '@eemp/ui';
import { getTranslations } from 'next-intl/server';

export default async function NotFound() {
  const t = await getTranslations('states');
  return (
    <main id="main-content">
      <StatePanel title={t('notFoundTitle')} description={t('notFoundBody')} />
    </main>
  );
}
