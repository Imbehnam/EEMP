'use client';

import type { FeeItem, FeeType, FinanceSettings } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { AmountField, Money, MoneyUnitToggle, useFinanceLabels } from '@eemp/app/finance';
import { useLocaleFormat } from '@eemp/i18n/react';
import {
  Alert, Badge, Button, Card, Checkbox, ConfirmDialog, DataTable, Fieldset, FormGrid, PageHeader, SelectField, Spinner, StatePanel, TextField,
} from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';

import { useFieldErrors } from '../../components/common';
import { useAcademicLookups, useTermLabel } from '../academic/options';
import { sectionTitle, useBranchNames, useTenantCurrency } from './common';

/* ======================= settings ======================= */

export function FinanceSettingsScreen() {
  const t = useTranslations('finance.settings');
  const common = useTranslations('common');
  const labels = useFinanceLabels();
  const { can } = useSession();
  const query = useApiQuery<FinanceSettings>('fin-settings', (api) => api.finance.settings.get());
  const [v, setV] = useState<FinanceSettings | null>(null);
  const [saved, setSaved] = useState(false);
  const [lateNotice, setLateNotice] = useState<string | null>(null);
  useEffect(() => {
    if (query.data) setV(query.data);
  }, [query.data]);
  const save = useApiMutation((api, s: FinanceSettings) => {
    const { currency: _c, availableGateways: _g, ...body } = s;
    return api.finance.settings.save(body);
  });
  const late = useApiMutation((api) => api.finance.lateFees.apply());
  const errors = useFieldErrors(save.error);
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  if (!v) return <Spinner label={common('loading')} />;
  const num = (value: string) => (value === '' ? 0 : Number(value));
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      {saved && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{t('saved')}</Alert>}
      <ErrorAlert error={save.error} onReload={() => { save.reset(); query.reload(); }} />
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setSaved(false);
          const result = await save.run(v);
          if (result.ok) {
            query.setData(result.value);
            setSaved(true);
          }
        }}
        style={{ display: 'grid', gap: 16 }}
      >
        <Card>
          <FormGrid>
            <TextField label={t('currency')} value={v.currency} readOnly dir="ltr" hint={t('currencyHint')} />
            <MoneyUnitToggle currency={v.currency} />
          </FormGrid>
        </Card>
        <Fieldset>
          <legend>{t('vat')}</legend>
          <FormGrid>
            <Checkbox label={t('vatEnabled')} checked={v.vatEnabled} onChange={(e) => setV({ ...v, vatEnabled: e.target.checked })} name="vatEnabled" />
            <TextField label={t('vatRate')} name="vatRatePercent" type="number" min={0} max={100} step={0.01} dir="ltr" disabled={!v.vatEnabled}
              value={v.vatRatePercent} hint={t('vatHint')} error={errors.vatRatePercent} onChange={(e) => setV({ ...v, vatRatePercent: num(e.target.value) })} />
          </FormGrid>
        </Fieldset>
        <Fieldset>
          <legend>{t('lateFees')}</legend>
          <FormGrid>
            <Checkbox label={t('lateFeeEnabled')} checked={v.lateFeeEnabled} onChange={(e) => setV({ ...v, lateFeeEnabled: e.target.checked })} name="lateFeeEnabled" />
            <SelectField label={t('lateFeeType')} name="lateFeeType" value={v.lateFeeType} disabled={!v.lateFeeEnabled}
              onChange={(e) => setV({ ...v, lateFeeType: e.target.value === 'percent' ? 'percent' : 'fixed' })}
              options={(['fixed', 'percent'] as const).map((x) => ({ value: x, label: labels.lateFeeType(x) }))} />
            <TextField label={t('lateFeeValue')} name="lateFeeValue" type="number" min={0} dir="ltr" disabled={!v.lateFeeEnabled} value={v.lateFeeValue}
              error={errors.lateFeeValue} onChange={(e) => setV({ ...v, lateFeeValue: num(e.target.value) })} />
            <TextField label={t('lateFeeGraceDays')} name="lateFeeGraceDays" type="number" min={0} dir="ltr" disabled={!v.lateFeeEnabled} value={v.lateFeeGraceDays}
              error={errors.lateFeeGraceDays} onChange={(e) => setV({ ...v, lateFeeGraceDays: num(e.target.value) })} />
          </FormGrid>
          {can('finance.invoices.manage') && query.data?.lateFeeEnabled && (
            <div style={{ marginBlockStart: 12, display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <Button variant="secondary" type="button" loading={late.busy}
                onClick={async () => {
                  const r = await late.run();
                  if (r.ok) setLateNotice(t('lateFeesApplied', { count: r.value.applied }));
                }}>{t('applyLateFees')}</Button>
              {lateNotice && <span role="status">{lateNotice}</span>}
              <ErrorAlert error={late.error} />
            </div>
          )}
        </Fieldset>
        <Fieldset>
          <legend>{t('sibling')}</legend>
          <TextField label={t('sibling')} name="siblingDiscountPercent" type="number" min={0} max={100} step={0.01} dir="ltr" value={v.siblingDiscountPercent}
            hint={t('siblingHint')} error={errors.siblingDiscountPercent} onChange={(e) => setV({ ...v, siblingDiscountPercent: num(e.target.value) })} />
        </Fieldset>
        <Fieldset>
          <legend>{t('onlinePayments')}</legend>
          <Checkbox label={t('onlinePaymentsEnabled')} name="onlinePaymentsEnabled" checked={v.onlinePaymentsEnabled} onChange={(e) => setV({ ...v, onlinePaymentsEnabled: e.target.checked })} />
          <p style={{ marginBlock: '12px 0' }} data-testid="gateways">
            {t('gateways')}:{' '}
            {v.availableGateways.length === 0 ? (
              <Badge tone="warning">{t('noGateway')}</Badge>
            ) : (
              v.availableGateways.map((g) => <Badge key={g} tone={g === 'fake' ? 'warning' : 'success'} style={{ marginInlineEnd: 4 }}>{t('gatewayName', { name: g })}</Badge>)
            )}
          </p>
        </Fieldset>
        <Fieldset>
          <legend>{t('results')}</legend>
          <Checkbox label={t('blockResults')} hint={t('blockResultsHint')} name="blockResultsOnOverdue" checked={v.blockResultsOnOverdue}
            onChange={(e) => setV({ ...v, blockResultsOnOverdue: e.target.checked })} />
        </Fieldset>
        <div>
          <Button type="submit" loading={save.busy}>{save.busy ? common('saving') : common('save')}</Button>
        </div>
      </form>
    </>
  );
}

/* ======================= fee types and items ======================= */

const EMPTY_TYPE = { code: '', name: '', taxable: false, isActive: true };

export function FeesScreen() {
  const t = useTranslations('finance.fees');
  const common = useTranslations('common');
  const { can } = useSession();
  const manage = can('finance.setup.manage');
  const types = useApiQuery<FeeType[]>('fee-types', (api) => api.finance.feeTypes.list());
  const items = useApiQuery<FeeItem[]>('fee-items', (api) => api.finance.feeItems.list());
  const termLabel = useTermLabel();
  const lookups = useAcademicLookups();
  const branches = useBranchNames();
  const currency = useTenantCurrency();
  const [typeForm, setTypeForm] = useState<(typeof EMPTY_TYPE & { id?: string; rowVersion?: string }) | null>(null);
  const [itemForm, setItemForm] = useState<{ id?: string; rowVersion?: string; feeTypeId: string; name: string; amount: number | null; termId: string; branchId: string; isActive: boolean } | null>(null);
  const [remove, setRemove] = useState<{ kind: 'type' | 'item'; id: string } | null>(null);
  const saveType = useApiMutation((api, f: NonNullable<typeof typeForm>) =>
    f.id ? api.finance.feeTypes.update(f.id, { ...f, rowVersion: f.rowVersion }) : api.finance.feeTypes.create(f));
  const saveItem = useApiMutation((api, f: NonNullable<typeof itemForm>) => {
    const body = { feeTypeId: f.feeTypeId, name: f.name, amount: f.amount ?? 0, termId: f.termId || null, branchId: f.branchId || null, isActive: f.isActive, rowVersion: f.rowVersion };
    return f.id ? api.finance.feeItems.update(f.id, body) : api.finance.feeItems.create(body);
  });
  const del = useApiMutation((api, r: NonNullable<typeof remove>) => (r.kind === 'type' ? api.finance.feeTypes.remove(r.id) : api.finance.feeItems.remove(r.id)));
  const typeErrors = useFieldErrors(saveType.error);
  const itemErrors = useFieldErrors(saveItem.error);
  if (types.error && !types.data) return <LoadError error={types.error} onRetry={types.reload} />;
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      {!manage && <Alert tone="info" style={{ marginBlockEnd: 12 }}>{t('readOnly')}</Alert>}
      <div style={{ display: 'grid', gap: 16 }}>
        <Card aria-labelledby="fee-types-h">
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap' }}>
            <h2 id="fee-types-h" style={sectionTitle}>{t('types')}</h2>
            {manage && <Button size="sm" onClick={() => { saveType.reset(); setTypeForm({ ...EMPTY_TYPE }); }}>{t('newType')}</Button>}
          </div>
          {typeForm && (
            <form
              onSubmit={async (e) => {
                e.preventDefault();
                const r = await saveType.run(typeForm);
                if (r.ok) { setTypeForm(null); types.reload(); }
              }}
              style={{ marginBlock: 12 }}
              aria-label={typeForm.id ? t('editType') : t('newType')}
            >
              <ErrorAlert error={saveType.error} onReload={() => { saveType.reset(); types.reload(); setTypeForm(null); }} />
              <FormGrid>
                <TextField label={t('code')} name="feeTypeCode" dir="ltr" required value={typeForm.code} error={typeErrors.code} onChange={(e) => setTypeForm({ ...typeForm, code: e.target.value })} />
                <TextField label={t('name')} name="feeTypeName" required value={typeForm.name} error={typeErrors.name} onChange={(e) => setTypeForm({ ...typeForm, name: e.target.value })} />
                <Checkbox label={t('taxable')} checked={typeForm.taxable} onChange={(e) => setTypeForm({ ...typeForm, taxable: e.target.checked })} />
                <Checkbox label={t('active')} checked={typeForm.isActive} onChange={(e) => setTypeForm({ ...typeForm, isActive: e.target.checked })} />
              </FormGrid>
              <div style={{ display: 'flex', gap: 8, marginBlockStart: 12 }}>
                <Button type="submit" loading={saveType.busy}>{common('save')}</Button>
                <Button type="button" variant="secondary" onClick={() => setTypeForm(null)}>{common('cancel')}</Button>
              </div>
            </form>
          )}
          <DataTable
            caption={t('types')}
            rows={types.data ?? []}
            loading={types.loading}
            loadingLabel={common('loading')}
            getRowKey={(r) => r.id}
            empty={<StatePanel title={t('emptyTypes')} />}
            columns={[
              { key: 'code', header: t('code'), isRowHeader: true, cell: (r) => <span className="eemp-ltr">{r.code}</span> },
              { key: 'name', header: t('name'), cell: (r) => r.name },
              { key: 'tax', header: t('taxable'), cell: (r) => (r.taxable ? common('yes') : common('no')) },
              { key: 'st', header: common('status'), cell: (r) => <Badge tone={r.isActive ? 'success' : 'neutral'}>{r.isActive ? common('active') : common('inactive')}</Badge> },
              ...(manage ? [{
                key: 'x', header: common('actions'),
                cell: (r: FeeType) => (
                  <span style={{ display: 'flex', gap: 4 }}>
                    <Button size="sm" variant="ghost" onClick={() => { saveType.reset(); setTypeForm({ ...r }); }}>{common('edit')}</Button>
                    <Button size="sm" variant="ghost" onClick={() => { del.reset(); setRemove({ kind: 'type', id: r.id }); }}>{common('delete')}</Button>
                  </span>
                ),
              }] : []),
            ]}
          />
        </Card>

        <Card aria-labelledby="fee-items-h">
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap' }}>
            <h2 id="fee-items-h" style={sectionTitle}>{t('items')}</h2>
            {manage && (
              <Button size="sm" disabled={(types.data ?? []).length === 0}
                onClick={() => { saveItem.reset(); setItemForm({ feeTypeId: types.data?.[0]?.id ?? '', name: '', amount: null, termId: '', branchId: '', isActive: true }); }}>
                {t('newItem')}
              </Button>
            )}
          </div>
          {itemForm && (
            <form
              onSubmit={async (e) => {
                e.preventDefault();
                const r = await saveItem.run(itemForm);
                if (r.ok) { setItemForm(null); items.reload(); }
              }}
              style={{ marginBlock: 12 }}
              aria-label={itemForm.id ? t('editItem') : t('newItem')}
            >
              <ErrorAlert error={saveItem.error} onReload={() => { saveItem.reset(); items.reload(); setItemForm(null); }} />
              <FormGrid>
                <SelectField label={t('type')} name="feeTypeId" required value={itemForm.feeTypeId} placeholder={t('chooseType')} error={itemErrors.feeTypeId}
                  onChange={(e) => setItemForm({ ...itemForm, feeTypeId: e.target.value })}
                  options={(types.data ?? []).map((x) => ({ value: x.id, label: `${x.code} · ${x.name}` }))} />
                <TextField label={t('name')} name="feeItemName" required value={itemForm.name} error={itemErrors.name} onChange={(e) => setItemForm({ ...itemForm, name: e.target.value })} />
                <AmountField label={t('amount')} name="feeItemAmount" required currency={currency} initial={itemForm.amount} error={itemErrors.amount}
                  onAmount={(amount) => setItemForm((f) => (f ? { ...f, amount } : f))} />
                <SelectField label={t('term')} name="termId" value={itemForm.termId} placeholder={t('anyTerm')} onChange={(e) => setItemForm({ ...itemForm, termId: e.target.value })}
                  options={lookups.terms.map((x) => ({ value: x.id, label: termLabel(x.id) }))} />
                <SelectField label={t('branch')} name="branchId" value={itemForm.branchId} placeholder={t('anyBranch')} onChange={(e) => setItemForm({ ...itemForm, branchId: e.target.value })}
                  options={branches.list.map((b) => ({ value: b.id, label: b.name }))} />
                <Checkbox label={t('active')} checked={itemForm.isActive} onChange={(e) => setItemForm({ ...itemForm, isActive: e.target.checked })} />
              </FormGrid>
              <div style={{ display: 'flex', gap: 8, marginBlockStart: 12 }}>
                <Button type="submit" loading={saveItem.busy} disabled={!itemForm.amount}>{common('save')}</Button>
                <Button type="button" variant="secondary" onClick={() => setItemForm(null)}>{common('cancel')}</Button>
              </div>
            </form>
          )}
          <DataTable
            caption={t('items')}
            rows={items.data ?? []}
            loading={items.loading}
            loadingLabel={common('loading')}
            getRowKey={(r) => r.id}
            empty={<StatePanel title={t('emptyItems')} />}
            columns={[
              { key: 'name', header: t('name'), isRowHeader: true, cell: (r) => r.name },
              { key: 'type', header: t('type'), cell: (r) => <span className="eemp-ltr">{r.feeTypeCode}</span> },
              { key: 'amount', header: t('amount'), cell: (r) => <Money amount={r.amount} currency={r.currency} /> },
              { key: 'term', header: t('term'), cell: (r) => (r.termId ? termLabel(r.termId) : t('anyTerm')) },
              { key: 'branch', header: t('branch'), cell: (r) => (r.branchId ? branches.name(r.branchId) : t('anyBranch')) },
              { key: 'st', header: common('status'), cell: (r) => <Badge tone={r.isActive ? 'success' : 'neutral'}>{r.isActive ? common('active') : common('inactive')}</Badge> },
              ...(manage ? [{
                key: 'x', header: common('actions'),
                cell: (r: FeeItem) => (
                  <span style={{ display: 'flex', gap: 4 }}>
                    <Button size="sm" variant="ghost" onClick={() => { saveItem.reset(); setItemForm({ id: r.id, rowVersion: r.rowVersion, feeTypeId: r.feeTypeId, name: r.name, amount: r.amount, termId: r.termId ?? '', branchId: r.branchId ?? '', isActive: r.isActive }); }}>{common('edit')}</Button>
                    <Button size="sm" variant="ghost" onClick={() => { del.reset(); setRemove({ kind: 'item', id: r.id }); }}>{common('delete')}</Button>
                  </span>
                ),
              }] : []),
            ]}
          />
        </Card>
      </div>
      <ConfirmDialog
        open={remove !== null}
        title={remove?.kind === 'type' ? t('deleteTypeTitle') : t('deleteItemTitle')}
        description={t('deleteBody')}
        confirmLabel={common('delete')}
        cancelLabel={common('cancel')}
        busy={del.busy}
        onCancel={() => setRemove(null)}
        onConfirm={async () => {
          if (!remove) return;
          const r = await del.run(remove);
          if (r.ok) {
            setRemove(null);
            types.reload();
            items.reload();
          }
        }}
      >
        <ErrorAlert error={del.error} />
      </ConfirmDialog>
    </>
  );
}


export function useFormatPercent() {
  const format = useLocaleFormat();
  return (value: number) => format.number(value / 100, { style: 'percent', maximumFractionDigits: 2 });
}
