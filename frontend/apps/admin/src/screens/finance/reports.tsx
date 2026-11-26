'use client';

import { AGING_BUCKETS, type AgingReport, type CollectionRow, type FinanceReportKind, type FinanceReportQuery, type OutstandingRow } from '@eemp/api-client';
import { DateField, LoadError, useApiQuery, useTodayIso, addDaysIso } from '@eemp/app';
import { FileDownloadButton, InvoiceNumber, Money, MoneyUnitToggle, useFinanceLabels } from '@eemp/app/finance';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link } from '@eemp/i18n/navigation';
import { Badge, Card, DataTable, FormGrid, PageHeader, SelectField, StatePanel } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState } from 'react';

import { useAcademicLookups, useTermLabel } from '../academic/options';
import { useBranchNames, useTenantCurrency } from './common';

export const REPORT_TABS: FinanceReportKind[] = ['outstanding', 'aging', 'collections'];

export function FinanceReportsScreen() {
  const t = useTranslations('finance.reports');
  const fc = useTranslations('finance.common');
  const today = useTodayIso();
  const branches = useBranchNames();
  const lookups = useAcademicLookups();
  const termLabel = useTermLabel();
  const currency = useTenantCurrency();
  const [tab, setTab] = useState<FinanceReportKind>('outstanding');
  const [branchId, setBranchId] = useState('');
  const [termId, setTermId] = useState('');
  const [asOf, setAsOf] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const query: FinanceReportQuery = {
    branchId: branchId || undefined,
    termId: tab === 'outstanding' ? termId || undefined : undefined,
    asOf: tab === 'aging' ? asOf || today : undefined,
    from: tab === 'collections' ? from || (addDaysIso(today, -30) ?? today) : undefined,
    to: tab === 'collections' ? to || today : undefined,
  };
  return (
    <>
      <PageHeader
        title={t('title')}
        description={t('intro')}
        actions={<FileDownloadButton testId="report-csv" labelKey="downloadCsv" load={(api) => api.finance.reports.csv(tab, query)} fallbackName={`${tab}.csv`} />}
      />
      <nav aria-label={t('title')} style={{ display: 'flex', gap: 4, flexWrap: 'wrap', marginBlockEnd: 16, borderBlockEnd: '1px solid var(--eemp-color-border)' }}>
        {REPORT_TABS.map((x) => (
          <button
            key={x}
            type="button"
            aria-current={x === tab ? 'page' : undefined}
            onClick={() => setTab(x)}
            style={{ padding: '8px 12px', background: 'none', border: 0, cursor: 'pointer', color: 'inherit', font: 'inherit', fontWeight: x === tab ? 700 : 500, borderBlockEnd: x === tab ? '3px solid var(--eemp-color-primary)' : '3px solid transparent' }}
          >
            {t('tab', { tab: x })}
          </button>
        ))}
      </nav>
      <FormGrid style={{ marginBlockEnd: 12 }}>
        {branches.list.length > 1 && (
          <SelectField label={fc('branch')} name="branchId" value={branchId} placeholder={fc('allBranches')} onChange={(e) => setBranchId(e.target.value)}
            options={branches.list.map((b) => ({ value: b.id, label: b.name }))} />
        )}
        {tab === 'outstanding' && (
          <SelectField label={fc('term')} name="termId" value={termId} placeholder={fc('anyTerm')} onChange={(e) => setTermId(e.target.value)}
            options={lookups.terms.map((x) => ({ value: x.id, label: termLabel(x.id) }))} />
        )}
        {tab === 'aging' && <DateField label={t('asOf')} name="asOf" value={asOf || today} onChange={setAsOf} />}
        {tab === 'collections' && (
          <>
            <DateField label={fc('from')} name="from" value={from || (addDaysIso(today, -30) ?? today)} onChange={setFrom} />
            <DateField label={fc('to')} name="to" value={to || today} onChange={setTo} />
          </>
        )}
        <MoneyUnitToggle currency={currency} />
      </FormGrid>
      {tab === 'outstanding' && <OutstandingReport query={query} />}
      {tab === 'aging' && <AgingReportView query={query} />}
      {tab === 'collections' && <CollectionsReport query={query} branchName={branches.name} />}
    </>
  );
}

const key = (q: FinanceReportQuery) => JSON.stringify(q);

function OutstandingReport({ query }: { query: FinanceReportQuery }) {
  const t = useTranslations('finance.reports');
  const fc = useTranslations('finance.common');
  const common = useTranslations('common');
  const format = useLocaleFormat();
  const currency = useTenantCurrency();
  const data = useApiQuery<OutstandingRow[]>(`rep-out|${key(query)}`, (api) => api.finance.reports.outstanding(query));
  if (data.error && !data.data) return <LoadError error={data.error} onRetry={data.reload} />;
  const rows = data.data ?? [];
  const sum = (f: (r: OutstandingRow) => number) => rows.reduce((s, r) => s + f(r), 0);
  return (
    <Card>
      <DataTable
        caption={t('tab', { tab: 'outstanding' })}
        rows={rows}
        loading={data.loading}
        loadingLabel={common('loading')}
        getRowKey={(r) => r.invoiceId}
        empty={<StatePanel title={t('empty')} />}
        columns={[
          { key: 'n', header: fc('number'), isRowHeader: true, cell: (r) => <Link href={`/finance/invoices/${r.invoiceId}`}><InvoiceNumber number={r.number} /></Link> },
          { key: 's', header: fc('student'), cell: (r) => <>{r.studentName} <span className="eemp-ltr">{r.studentNumber}</span></> },
          { key: 'd', header: fc('dueDate'), cell: (r) => <>{r.dueDate ? format.calendarDate(r.dueDate) : fc('none')} {r.isOverdue && <Badge tone="danger">{fc('overdue')}</Badge>}</> },
          { key: 't', header: fc('total'), cell: (r) => <Money amount={r.total} currency={currency} /> },
          { key: 'p', header: fc('paid'), cell: (r) => <Money amount={r.paid} currency={currency} /> },
          { key: 'b', header: fc('balance'), cell: (r) => <Money amount={r.balance} currency={currency} /> },
        ]}
      />
      {rows.length > 0 && (
        <p style={{ marginBlock: '12px 0' }} data-testid="outstanding-total">
          {t('totals')}: <Money amount={sum((r) => r.balance)} currency={currency} strong />
        </p>
      )}
    </Card>
  );
}

function AgingReportView({ query }: { query: FinanceReportQuery }) {
  const t = useTranslations('finance.reports');
  const fc = useTranslations('finance.common');
  const common = useTranslations('common');
  const labels = useFinanceLabels();
  const data = useApiQuery<AgingReport>(`rep-aging|${key(query)}`, (api) => api.finance.reports.aging(query));
  if (data.error && !data.data) return <LoadError error={data.error} onRetry={data.reload} />;
  const report = data.data;
  const c = report?.currency ?? 'IRR';
  return (
    <Card>
      {report && (
        <div style={{ display: 'grid', gap: 8, gridTemplateColumns: 'repeat(auto-fit, minmax(9rem, 1fr))', marginBlockEnd: 16 }} data-testid="aging-buckets">
          {AGING_BUCKETS.map((b) => (
            <div key={b}>
              <div style={{ fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)' }}>{labels.agingBucket(b)}</div>
              <Money amount={report.buckets[b]} currency={c} strong />
            </div>
          ))}
        </div>
      )}
      <DataTable
        caption={t('tab', { tab: 'aging' })}
        rows={report?.rows ?? []}
        loading={data.loading}
        loadingLabel={common('loading')}
        getRowKey={(r) => r.studentId}
        empty={<StatePanel title={t('empty')} />}
        columns={[
          { key: 's', header: fc('student'), isRowHeader: true, cell: (r) => <Link href={`/finance/accounts/${r.studentId}`}>{r.studentName}</Link> },
          ...AGING_BUCKETS.map((b) => ({ key: b, header: labels.agingBucket(b), cell: (r: AgingReport['rows'][number]) => <Money amount={r.buckets[b]} currency={c} /> })),
          { key: 't', header: t('bucketTotal'), cell: (r) => <Money amount={r.buckets.total} currency={c} strong /> },
        ]}
      />
    </Card>
  );
}

function CollectionsReport({ query, branchName }: { query: FinanceReportQuery; branchName: (id: string) => string }) {
  const t = useTranslations('finance.reports');
  const fc = useTranslations('finance.common');
  const common = useTranslations('common');
  const labels = useFinanceLabels();
  const format = useLocaleFormat();
  const currency = useTenantCurrency();
  const data = useApiQuery<CollectionRow[]>(`rep-coll|${key(query)}`, (api) => api.finance.reports.collections(query));
  if (data.error && !data.data) return <LoadError error={data.error} onRetry={data.reload} />;
  const rows = data.data ?? [];
  return (
    <Card>
      <DataTable
        caption={t('tab', { tab: 'collections' })}
        rows={rows}
        loading={data.loading}
        loadingLabel={common('loading')}
        getRowKey={(r) => `${r.date}|${r.branchId}|${r.method}`}
        empty={<StatePanel title={t('empty')} />}
        columns={[
          { key: 'd', header: fc('date'), isRowHeader: true, cell: (r) => format.calendarDate(r.date) },
          { key: 'b', header: fc('branch'), cell: (r) => branchName(r.branchId) },
          { key: 'm', header: fc('method'), cell: (r) => labels.method(r.method) },
          { key: 'c', header: t('count'), cell: (r) => format.number(r.count) },
          { key: 'a', header: fc('amount'), cell: (r) => <Money amount={r.amount} currency={currency} /> },
        ]}
      />
      {rows.length > 0 && (
        <p style={{ marginBlock: '12px 0' }} data-testid="collections-total">
          {t('totals')}: <Money amount={rows.reduce((s, r) => s + r.amount, 0)} currency={currency} strong />
        </p>
      )}
    </Card>
  );
}
