'use client';

import type { ReportCard, ReportCardSummary } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession, useTodayIso } from '@eemp/app';
import { PassBadge, personName, PrintButton, ReportCardContentView, ScoreEntryPanel, useAcademicLabels } from '@eemp/app/academic';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link, useRouter } from '@eemp/i18n/navigation';
import { Alert, Badge, Button, Card, ConfirmDialog, DataTable, FormGrid, PageHeader, SelectField, Spinner, StatePanel } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';

import { BackLink, PagedList } from '../../components/common';
import { useAcademicLookups } from './options';

/* ======================= assessment: scores + workflow ======================= */

export function AssessmentScreen({ id }: { id: string }) {
  const t = useTranslations('academic.scores');
  const common = useTranslations('common');
  const router = useRouter();
  const { can } = useSession();
  const assessment = useApiQuery(`assessment|${id}`, (api) => api.academic.assessments.get(id));
  const cls = useApiQuery(`assessment-class|${assessment.data?.classId ?? ''}`, (api) =>
    assessment.data ? api.academic.classes.get(assessment.data.classId) : Promise.resolve(null),
  );
  if (assessment.error && !assessment.data) return <LoadError error={assessment.error} onRetry={assessment.reload} />;
  const a = assessment.data;
  if (!a) return <Spinner label={common('loading')} />;
  const classHref = `/academic/classes/${a.classId}?tab=assessments`;
  return (
    <>
      <PageHeader before={<BackLink href={classHref} />} title={a.name} description={cls.data ? t('ofClass', { name: cls.data.name }) : undefined} />
      <ScoreEntryPanel
        assessmentId={id}
        perms={{ manage: can('grades.manage'), approve: can('grades.approve'), publish: can('grades.publish') }}
        onDeleted={() => router.push(`${classHref}&notice=deleted`)}
      />
    </>
  );
}

/* ======================= report cards ======================= */

export function ReportCardsScreen() {
  const t = useTranslations('academic.reportCards');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  const { can } = useSession();
  const lookups = useAcademicLookups();
  const [termId, setTermId] = useState('');
  const [classId, setClassId] = useState('');
  const [status, setStatus] = useState('');
  const [tick, setTick] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<'generate' | 'publish' | null>(null);
  const today = useTodayIso();
  useEffect(() => {
    if (!termId && lookups.terms.length) {
      setTermId((lookups.terms.find((x) => x.startDate <= today && today <= x.endDate) ?? lookups.terms[0])!.id);
    }
  }, [lookups.terms, termId, today]);
  const classes = useApiQuery(`rc-classes|${termId}|${can('classes.read')}`, async (api) =>
    termId && can('classes.read') ? (await api.academic.classes.list({ termId, pageSize: 100 })).items : [],
  );
  const generate = useApiMutation((api) => api.academic.reportCards.generate({ termId, classId: classId || null }));
  const publish = useApiMutation((api) => api.academic.reportCards.publishBatch({ termId, classId: classId || null }));
  const termOptions = lookups.terms.map((x) => ({ value: x.id, label: `${lookups.years.find((y) => y.id === x.academicYearId)?.name ?? ''} · ${x.name}` }));
  return (
    <>
      <PageHeader
        title={t('title')}
        description={t('intro')}
        actions={
          <>
            {can('reportcards.manage') && <Button disabled={!termId} onClick={() => { generate.reset(); setConfirm('generate'); }}>{t('generate')}</Button>}
            {can('reportcards.publish') && <Button variant="secondary" disabled={!termId} onClick={() => { publish.reset(); setConfirm('publish'); }}>{t('publishAll')}</Button>}
          </>
        }
      />
      {notice && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{notice}</Alert>}
      <FormGrid style={{ marginBlockEnd: 12 }}>
        <SelectField label={t('term')} name="term" required value={termId} placeholder={t('chooseTerm')} onChange={(e) => { setTermId(e.target.value); setClassId(''); }} options={termOptions} />
        <SelectField
          label={t('class')} name="class" value={classId} placeholder={t('allClasses')} onChange={(e) => setClassId(e.target.value)}
          options={(classes.data ?? []).map((c) => ({ value: c.id, label: `${c.name} (${c.code})` }))}
        />
        <SelectField
          label={common('status')} name="status" value={status} placeholder={t('allStatuses')} onChange={(e) => setStatus(e.target.value)}
          options={['draft', 'published'].map((s) => ({ value: s, label: labels.reportCardStatus(s) }))}
        />
      </FormGrid>
      {termId && (
        <PagedList<ReportCardSummary>
          queryKey={`report-cards|${termId}|${classId}|${status}|${tick}`}
          load={(api, q) => api.academic.reportCards.list({ ...q, termId, classId: classId || undefined, status: status || undefined })}
          caption={t('title')}
          searchLabel={t('searchLabel')}
          emptyText={t('empty')}
          getRowKey={(r) => r.id}
          columns={[
            { key: 'student', header: t('student'), isRowHeader: true, cell: (r) => <Link href={`/academic/report-cards/${r.id}`}>{personName(r)}</Link> },
            { key: 'number', header: t('studentNumber'), cell: (r) => <span className="eemp-ltr">{r.studentNumber}</span> },
            { key: 'average', header: t('average'), cell: (r) => (r.average ? <>{labels.total(r.average)} <PassBadge total={r.average} /></> : common('none')) },
            { key: 'version', header: t('version'), cell: (r) => t('versionValue', { current: r.currentVersion, published: r.publishedVersion ?? 0 }) },
            { key: 'status', header: common('status'), cell: (r) => <Badge tone={r.status === 'published' ? 'success' : 'neutral'}>{labels.reportCardStatus(r.status)}</Badge> },
            { key: 'generated', header: t('generatedAt'), cell: (r) => format.dateTime(r.generatedAt) },
          ]}
        />
      )}
      <ConfirmDialog
        open={confirm !== null}
        tone="primary"
        title={confirm === 'generate' ? t('generateTitle') : t('publishAllTitle')}
        description={confirm === 'generate' ? t('generateBody') : t('publishAllBody')}
        confirmLabel={confirm === 'generate' ? t('generate') : t('publishAll')}
        cancelLabel={common('cancel')}
        busy={generate.busy || publish.busy}
        onCancel={() => setConfirm(null)}
        onConfirm={async () => {
          if (confirm === 'generate') {
            const result = await generate.run();
            if (result.ok) setNotice(t('generated', { created: result.value.created, regenerated: result.value.regenerated, unchanged: result.value.unchanged }));
            if (result.ok) setConfirm(null);
          } else {
            const result = await publish.run();
            if (result.ok) {
              setNotice(t('publishedCount', { count: result.value.published }));
              setConfirm(null);
            }
          }
          setTick((x) => x + 1);
        }}
      >
        <ErrorAlert error={generate.error ?? publish.error} />
      </ConfirmDialog>
    </>
  );
}

export function ReportCardDetailScreen({ id }: { id: string }) {
  const t = useTranslations('academic.reportCards');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  const { can } = useSession();
  const query = useApiQuery<ReportCard>(`report-card|${id}`, (api) => api.academic.reportCards.get(id));
  const [versionNo, setVersionNo] = useState<number | null>(null);
  const version = useApiQuery(`report-card-version|${id}|${versionNo ?? ''}`, (api) =>
    versionNo ? api.academic.reportCards.version(id, versionNo) : Promise.resolve(null),
  );
  const [confirm, setConfirm] = useState(false);
  const [notice, setNotice] = useState(false);
  const publish = useApiMutation((api, card: ReportCard) => api.academic.reportCards.publish(card.id, { rowVersion: card.rowVersion }));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const card = query.data;
  if (!card) return <Spinner label={common('loading')} />;
  const shown = versionNo && version.data ? version.data.content : card.content;
  return (
    <>
      <PageHeader
        before={<BackLink href="/academic/report-cards" />}
        title={t('detailTitle', { name: personName(card) })}
        actions={
          <>
            {can('reportcards.publish') && card.status === 'draft' && <Button onClick={() => { publish.reset(); setConfirm(true); }}>{t('publish')}</Button>}
            <PrintButton />
          </>
        }
      />
      {notice && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{t('publishedOne')}</Alert>}
      <ErrorAlert error={publish.error} onReload={() => { publish.reset(); query.reload(); }} />
      <div style={{ display: 'grid', gap: 16, gridTemplateColumns: 'minmax(0, 3fr) minmax(14rem, 1fr)' }} className="eemp-rc-layout">
        <Card>
          {versionNo && version.loading ? <Spinner label={common('loading')} /> : <ReportCardContentView content={shown} />}
        </Card>
        <Card aria-labelledby="versions-heading" className="eemp-no-print">
          <h2 id="versions-heading" style={{ fontSize: '1rem', marginBlockStart: 0 }}>{t('versions')}</h2>
          <DataTable
            caption={t('versions')}
            rows={[...card.versions].reverse()}
            getRowKey={(v) => String(v.versionNo)}
            empty={<StatePanel title={t('empty')} />}
            columns={[
              {
                key: 'v', header: t('version'), isRowHeader: true,
                cell: (v) => (
                  <Button size="sm" variant={(versionNo ?? card.currentVersion) === v.versionNo ? 'primary' : 'ghost'} aria-pressed={(versionNo ?? card.currentVersion) === v.versionNo}
                    onClick={() => setVersionNo(v.versionNo === card.currentVersion ? null : v.versionNo)}>
                    {t('versionNo', { n: v.versionNo })}
                  </Button>
                ),
              },
              { key: 's', header: common('status'), cell: (v) => labels.reportCardStatus(v.status) },
              { key: 'p', header: t('publishedAt'), cell: (v) => (v.publishedAt ? format.dateTime(v.publishedAt) : common('none')) },
            ]}
          />
        </Card>
      </div>
      <ConfirmDialog
        open={confirm}
        tone="primary"
        title={t('publishTitle')}
        description={t('publishBody')}
        confirmLabel={t('publish')}
        cancelLabel={common('cancel')}
        busy={publish.busy}
        onCancel={() => setConfirm(false)}
        onConfirm={async () => {
          const result = await publish.run(card);
          setConfirm(false);
          if (result.ok) {
            query.setData(result.value);
            setNotice(true);
          }
        }}
      />
    </>
  );
}
