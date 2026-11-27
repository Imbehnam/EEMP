'use client';

import {
  ASSESSMENT_TYPES, ApiError, RESULT_MARKS,
  type Assessment, type AssessmentRequest, type AssessmentResults, type GradeHistoryEntry, type AssessmentType, type ResultEntry, type ResultMark, type WorkflowAction,
} from '@eemp/api-client';
import { useErrorTranslator, useLocaleFormat } from '@eemp/i18n/react';
import { Alert, Button, Card, ConfirmDialog, DataTable, DescriptionList, FormGrid, SelectField, Spinner, TextAreaField, TextField } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useEffect, useMemo, useState, type FormEvent } from 'react';

import { DateField } from '../DateField';
import { ErrorAlert, LoadError, toApiErrorLike } from '../ErrorAlert';
import { useApiMutation, useApiQuery } from '../use-api';
import { AssessmentStatusBadge, personName, useAcademicLabels } from './labels';

export interface GradePermissions {
  /** Enter scores, edit/delete drafts, submit: grades.manage in branch or the assigned teacher. */
  manage: boolean;
  /** Approve, return, reopen: grades.approve. */
  approve: boolean;
  /** Publish: grades.publish. */
  publish: boolean;
}

type Row = { mark: ResultMark | ''; score: string; comment: string };

/* ---------------- assessment form (create / edit details) ---------------- */

export interface AssessmentFormValues {
  name: string;
  type: AssessmentType;
  weight: string;
  maxScore: string;
  date: string;
}

export const assessmentValues = (a?: Assessment): AssessmentFormValues => ({
  name: a?.name ?? '',
  type: (a?.type as AssessmentType) ?? 'quiz',
  weight: a ? String(a.weight) : '1',
  maxScore: a ? String(a.maxScore) : '20',
  date: a?.date ?? '',
});

export const toAssessmentRequest = (v: AssessmentFormValues): AssessmentRequest => ({
  name: v.name.trim(),
  type: v.type,
  weight: Number(v.weight),
  maxScore: Number(v.maxScore),
  date: v.date || null,
});

export function AssessmentFields({ v, set, errors }: { v: AssessmentFormValues; set: (v: AssessmentFormValues) => void; errors: Record<string, string> }) {
  const t = useTranslations('academic.assessments');
  const labels = useAcademicLabels();
  return (
    <FormGrid>
      <TextField label={t('name')} name="name" required value={v.name} error={errors.name} onChange={(e) => set({ ...v, name: e.target.value })} />
      <SelectField
        label={t('type')}
        name="type"
        value={v.type}
        error={errors.type}
        onChange={(e) => set({ ...v, type: e.target.value as AssessmentType })}
        options={ASSESSMENT_TYPES.map((value) => ({ value, label: labels.assessmentType(value) }))}
      />
      <TextField
        label={t('weight')} name="weight" type="number" step="0.01" min="0.01" max="100" required dir="ltr"
        hint={t('weightHint')} value={v.weight} error={errors.weight} onChange={(e) => set({ ...v, weight: e.target.value })}
      />
      <TextField
        label={t('maxScore')} name="maxScore" type="number" step="0.01" min="0.01" max="1000" required dir="ltr"
        value={v.maxScore} error={errors.maxScore} onChange={(e) => set({ ...v, maxScore: e.target.value })}
      />
      <DateField label={t('date')} name="date" value={v.date} error={errors.date} onChange={(date) => set({ ...v, date })} />
    </FormGrid>
  );
}

/* ---------------- score entry ---------------- */

/**
 * Score entry and grade workflow for one assessment: results grid (mark + score + comment), save with an
 * optional reason (written to the grade history), workflow actions gated by permission (UX only — the API
 * re-checks every transition), and the append-only grade history.
 */
export function ScoreEntryPanel({ assessmentId, perms, onDeleted }: { assessmentId: string; perms: GradePermissions; onDeleted?: () => void }) {
  const t = useTranslations('academic.scores');
  const wf = useTranslations('academic.workflow');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const errorText = useErrorTranslator();
  const query = useApiQuery(`results|${assessmentId}`, (api) => api.academic.assessments.results(assessmentId));
  const history = useApiQuery(`history|${assessmentId}`, (api) => api.academic.assessments.history(assessmentId));
  const data = query.data;
  const [rows, setRows] = useState<Record<string, Row>>({});
  const [reason, setReason] = useState('');
  const [notice, setNotice] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);
  const [pending, setPending] = useState<WorkflowAction | null>(null);
  const [actionReason, setActionReason] = useState('');
  const [confirmDelete, setConfirmDelete] = useState(false);

  useEffect(() => {
    if (!data) return;
    setRows(Object.fromEntries(data.results.map((r) => [r.studentId, {
      mark: (r.mark as ResultMark | null) ?? '', score: r.score === null ? '' : String(r.score), comment: r.comment ?? '',
    }])));
  }, [data]);

  const save = useApiMutation((api, current: AssessmentResults, entries: ResultEntry[]) =>
    api.academic.assessments.saveResults(assessmentId, { rowVersion: current.assessment.rowVersion, results: entries }),
  );
  const transition = useApiMutation((api, a: Assessment, action: WorkflowAction, why: string) =>
    api.academic.assessments.transition(a.id, action, { rowVersion: a.rowVersion, reason: why.trim() || null }),
  );
  const update = useApiMutation((api, a: Assessment, v: AssessmentFormValues) =>
    api.academic.assessments.update(a.id, { ...toAssessmentRequest(v), rowVersion: a.rowVersion }),
  );
  const remove = useApiMutation((api, id: string) => api.academic.assessments.remove(id));
  const [details, setDetails] = useState<AssessmentFormValues | null>(null);

  /** Field errors from `results[i].field` mapped back to the student of the i-th sent row. */
  const [sent, setSent] = useState<string[]>([]);
  const rowErrors = useMemo(() => {
    const out: Record<string, string> = {};
    if (!save.error) return out;
    for (const [key, codes] of Object.entries(toApiErrorLike(save.error).fieldErrors)) {
      const m = /^results\[(\d+)\]\.(\w+)$/.exec(key);
      const sid = m ? sent[Number(m[1])] : undefined;
      if (sid && codes[0]) out[`${sid}.${m![2]}`] = errorText.code(codes[0]);
    }
    return out;
  }, [save.error, sent, errorText]);

  if (query.error && !data) return <LoadError error={query.error} onRetry={query.reload} />;
  if (!data) return <Spinner label={common('loading')} />;
  const a = data.assessment;
  const editable = a.status === 'draft' && perms.manage;
  const names = new Map(data.results.map((r) => [r.studentId, personName(r)]));

  const changedEntries = (): ResultEntry[] =>
    data.results.flatMap((r) => {
      const row = rows[r.studentId];
      if (!row || !row.mark) return [];
      const score = row.mark === 'scored' ? (row.score.trim() === '' ? null : Number(row.score)) : null;
      const same = row.mark === r.mark && (row.mark !== 'scored' || score === r.score) && (row.comment.trim() || null) === (r.comment || null);
      if (same) return [];
      return [{ studentId: r.studentId, mark: row.mark, score, comment: row.comment.trim() || null, reason: reason.trim() || null }];
    });

  const actions: { action: WorkflowAction; allowed: boolean; needsReason: boolean; tone: 'primary' | 'danger' }[] = [
    { action: 'submit', allowed: a.status === 'draft' && perms.manage, needsReason: false, tone: 'primary' },
    { action: 'approve', allowed: a.status === 'submitted' && perms.approve, needsReason: false, tone: 'primary' },
    { action: 'return', allowed: a.status === 'submitted' && perms.approve, needsReason: true, tone: 'danger' },
    { action: 'publish', allowed: a.status === 'approved' && perms.publish, needsReason: false, tone: 'primary' },
    { action: 'reopen', allowed: a.status === 'published' && perms.approve, needsReason: true, tone: 'danger' },
  ];
  const pendingAction = actions.find((x) => x.action === pending);

  return (
    <>
      <Card aria-labelledby="assessment-heading">
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', alignItems: 'center' }}>
          <h2 id="assessment-heading" style={{ fontSize: '1.0625rem', margin: 0 }}>{t('detailsTitle')}</h2>
          <span data-testid="assessment-status"><AssessmentStatusBadge status={a.status} /></span>
        </div>
        {notice && <Alert tone="success" role="status" style={{ marginBlock: 12 }}>{notice}</Alert>}
        <ErrorAlert error={transition.error} onReload={() => { transition.reset(); query.reload(); }} />
        {a.returnReason && a.status === 'draft' && (
          <Alert tone="warning" style={{ marginBlock: 12 }} title={t('returnedTitle')}>{a.returnReason}</Alert>
        )}
        {editing && details ? (
          <form
            onSubmit={async (e: FormEvent) => {
              e.preventDefault();
              const result = await update.run(a, details);
              if (result.ok) {
                setEditing(false);
                setNotice(common('saved'));
                query.reload();
              }
            }}
          >
            <ErrorAlert error={update.error} onReload={() => { update.reset(); query.reload(); }} />
            <AssessmentFields v={details} set={setDetails} errors={update.error ? errorText.fields(toApiErrorLike(update.error).fieldErrors) : {}} />
            <div style={{ display: 'flex', gap: 8, marginBlockStart: 12 }}>
              <Button type="submit" loading={update.busy}>{common('save')}</Button>
              <Button type="button" variant="secondary" onClick={() => setEditing(false)}>{common('cancel')}</Button>
            </div>
          </form>
        ) : (
          <DescriptionList style={{ marginBlock: 12 }}>
            <dt>{t('type')}</dt>
            <dd>{labels.assessmentType(a.type)}</dd>
            <dt>{t('weight')}</dt>
            <dd>{labels.number(a.weight)}</dd>
            <dt>{t('maxScore')}</dt>
            <dd>{labels.number(a.maxScore)}</dd>
            <dt>{t('date')}</dt>
            <AssessmentDate date={a.date} />
          </DescriptionList>
        )}
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {actions.filter((x) => x.allowed).map((x) => (
            <Button
              key={x.action}
              variant={x.tone === 'danger' ? 'secondary' : 'primary'}
              data-action={x.action}
              onClick={() => { transition.reset(); setActionReason(''); setPending(x.action); }}
            >
              {wf('action', { action: x.action })}
            </Button>
          ))}
          {editable && !editing && (
            <Button variant="secondary" onClick={() => { setDetails(assessmentValues(a)); setEditing(true); }}>{t('editDetails')}</Button>
          )}
          {editable && !a.publishedAt && (
            <Button variant="danger" onClick={() => setConfirmDelete(true)}>{common('delete')}</Button>
          )}
        </div>
        {!editable && a.status !== 'draft' && <p style={{ fontSize: '0.875rem', marginBlockEnd: 0 }}>{t('lockedHint')}</p>}
      </Card>

      <Card aria-labelledby="scores-heading" style={{ marginBlockStart: 16 }}>
        <h2 id="scores-heading" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('title')}</h2>
        <ErrorAlert error={save.error} onReload={() => { save.reset(); query.reload(); }} />
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            setNotice(null);
            const entries = changedEntries();
            if (entries.length === 0) {
              setNotice(t('nothingChanged'));
              return;
            }
            setSent(entries.map((x) => x.studentId));
            const result = await save.run(data, entries);
            if (result.ok) {
              query.setData(result.value);
              setReason('');
              setNotice(t('saved', { count: entries.length }));
              history.reload();
            }
          }}
        >
          <div style={{ overflowX: 'auto' }}>
            <table className="eemp-matrix" style={{ inlineSize: '100%', borderCollapse: 'collapse' }}>
              <caption className="eemp-visually-hidden">{t('caption', { name: a.name })}</caption>
              <thead>
                <tr>
                  <th scope="col" style={{ textAlign: 'start' }}>{t('student')}</th>
                  <th scope="col" style={{ textAlign: 'start' }}>{t('mark')}</th>
                  <th scope="col" style={{ textAlign: 'start' }}>{t('score', { max: labels.number(a.maxScore) })}</th>
                  <th scope="col" style={{ textAlign: 'start' }}>{t('comment')}</th>
                </tr>
              </thead>
              <tbody>
                {data.results.map((r) => {
                  const row = rows[r.studentId] ?? { mark: '', score: '', comment: '' };
                  const name = personName(r);
                  const set = (patch: Partial<Row>) => setRows((all) => ({ ...all, [r.studentId]: { ...row, ...patch } }));
                  const scoreError = rowErrors[`${r.studentId}.score`] ?? rowErrors[`${r.studentId}.mark`];
                  return (
                    <tr key={r.studentId} data-student={r.studentNumber}>
                      <th scope="row" style={{ textAlign: 'start', fontWeight: 500 }}>
                        {name} <span className="eemp-ltr" style={{ color: 'var(--eemp-color-fg-muted)' }}>{r.studentNumber}</span>
                      </th>
                      <td>
                        <select
                          aria-label={t('markFor', { name })}
                          value={row.mark}
                          disabled={!editable}
                          onChange={(e) => set({ mark: e.target.value as ResultMark | '', score: e.target.value === 'scored' ? row.score : '' })}
                          style={cellControl}
                        >
                          <option value="">{t('noMark')}</option>
                          {RESULT_MARKS.map((m) => <option key={m} value={m}>{labels.mark(m)}</option>)}
                        </select>
                      </td>
                      <td>
                        <input
                          aria-label={t('scoreFor', { name })}
                          type="number"
                          inputMode="decimal"
                          step="0.01"
                          min={0}
                          max={a.maxScore}
                          dir="ltr"
                          value={row.score}
                          disabled={!editable || (row.mark !== '' && row.mark !== 'scored')}
                          aria-invalid={scoreError ? true : undefined}
                          onChange={(e) => set({ score: e.target.value, mark: row.mark || 'scored' })}
                          style={{ ...cellControl, maxInlineSize: '7rem' }}
                        />
                        {!editable && r.score !== null && <span className="eemp-visually-hidden">{labels.number(r.score)}</span>}
                        {scoreError && <div role="alert" style={{ color: 'var(--eemp-color-danger)', fontSize: '0.8125rem' }}>{scoreError}</div>}
                      </td>
                      <td>
                        <input
                          aria-label={t('commentFor', { name })}
                          value={row.comment}
                          maxLength={500}
                          disabled={!editable}
                          onChange={(e) => set({ comment: e.target.value })}
                          style={cellControl}
                        />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          {data.results.length === 0 && <p>{t('noStudents')}</p>}
          {editable && data.results.length > 0 && (
            <div style={{ marginBlockStart: 12 }}>
              <TextField label={t('reason')} hint={t('reasonHint')} name="reason" value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />
              <div style={{ marginBlockStart: 12 }}>
                <Button type="submit" loading={save.busy}>{save.busy ? common('saving') : t('save')}</Button>
              </div>
            </div>
          )}
        </form>
      </Card>

      <Card aria-labelledby="history-heading" style={{ marginBlockStart: 16 }}>
        <h2 id="history-heading" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('historyTitle')}</h2>
        <HistoryTable entries={history.data ?? []} loading={history.loading} names={names} />
      </Card>

      <ConfirmDialog
        open={pending !== null}
        tone={pendingAction?.tone ?? 'primary'}
        title={pending ? wf('confirmTitle', { action: pending }) : ''}
        description={pending ? wf('confirmBody', { action: pending }) : undefined}
        confirmLabel={pending ? wf('action', { action: pending }) : ''}
        cancelLabel={common('cancel')}
        busy={transition.busy}
        onCancel={() => { transition.reset(); setPending(null); }}
        onConfirm={async () => {
          if (!pending) return;
          if (pendingAction?.needsReason && !actionReason.trim()) return;
          const result = await transition.run(a, pending, actionReason);
          if (result.ok) {
            setNotice(wf('done', { action: pending }));
            setPending(null);
            query.reload();
            history.reload();
          }
        }}
      >
        {pendingAction?.needsReason && (
          <TextAreaField
            label={wf('reason')}
            required
            rows={3}
            maxLength={500}
            value={actionReason}
            error={transition.error instanceof ApiError ? errorText.fields(transition.error.fieldErrors).reason : undefined}
            onChange={(e) => setActionReason(e.target.value)}
          />
        )}
        <ErrorAlert error={transition.error} />
      </ConfirmDialog>
      <ConfirmDialog
        open={confirmDelete}
        title={common('confirmDeleteTitle', { name: a.name })}
        description={common('confirmDeleteBody')}
        confirmLabel={common('delete')}
        cancelLabel={common('cancel')}
        busy={remove.busy}
        onCancel={() => { remove.reset(); setConfirmDelete(false); }}
        onConfirm={async () => {
          const result = await remove.run(a.id);
          if (result.ok) {
            setConfirmDelete(false);
            onDeleted?.();
          }
        }}
      >
        <ErrorAlert error={remove.error} />
      </ConfirmDialog>
    </>
  );
}

function AssessmentDate({ date }: { date?: string | null }) {
  const format = useLocaleFormat();
  const common = useTranslations('common');
  return <dd>{date ? format.calendarDate(date) : common('none')}</dd>;
}

const cellControl = {
  padding: '4px 6px', border: '1px solid var(--eemp-color-border)', borderRadius: 6, font: 'inherit',
  background: 'var(--eemp-color-surface)', color: 'inherit', inlineSize: '100%',
} as const;

function HistoryTable({ entries, loading, names }: { entries: GradeHistoryEntry[]; loading: boolean; names: Map<string, string> }) {
  const t = useTranslations('academic.scores');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  const value = (score: number | null, mark: string | null) =>
    mark === 'scored' || (mark === null && score !== null) ? labels.number(score) : mark ? labels.mark(mark) : common('none');
  return (
    <DataTable
      caption={t('historyTitle')}
      rows={[...entries].reverse()}
      loading={loading}
      loadingLabel={common('loading')}
      getRowKey={(h) => `${h.occurredAt}|${h.action}|${h.studentId ?? ''}`}
      empty={<p>{t('historyEmpty')}</p>}
      columns={[
        { key: 'when', header: t('when'), cell: (h) => format.dateTime(h.occurredAt) },
        { key: 'action', header: t('action'), cell: (h) => labels.historyAction(h.action) },
        { key: 'student', header: t('student'), cell: (h) => (h.studentId ? (names.get(h.studentId) ?? common('none')) : common('none')) },
        {
          key: 'change', header: t('change'),
          cell: (h) => (h.action === 'scoreChanged' ? t('changeValue', { from: value(h.oldScore, h.oldMark), to: value(h.newScore, h.newMark) }) : common('none')),
        },
        { key: 'reason', header: t('reasonColumn'), cell: (h) => h.reason || common('none') },
      ]}
    />
  );
}
