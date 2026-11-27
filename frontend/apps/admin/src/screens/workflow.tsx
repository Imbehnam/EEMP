'use client';

import {
  newIdempotencyKey, WORKFLOW_INSTANCE_STATUSES, type Permission, type WorkflowCommand, type WorkflowDefinition, type WorkflowDefinitionSummary,
  type WorkflowInstance, type WorkflowInstanceSummary, type WorkflowStepInput, type WorkflowVersion,
} from '@eemp/api-client';
import { ButtonLink, ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link, useRouter } from '@eemp/i18n/navigation';
import {
  Alert, Badge, Button, Card, Checkbox, ConfirmDialog, DataTable, DescriptionList, FormGrid, PageHeader, SelectField, Spinner, StatePanel, TextAreaField,
  TextField, type Tone,
} from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useEffect, useMemo, useState } from 'react';

import { BackLink, Notice, PagedList, useFieldErrors } from '../components/common';
import { useUserNames } from './finance/common';

const STATUS_TONE: Record<string, Tone> = { pending: 'warning', returned: 'info', approved: 'success', rejected: 'danger', cancelled: 'neutral' };

function useWorkflowLabels() {
  const t = useTranslations('workflow.enums');
  return useMemo(
    () => ({
      status: (value: string) => t('status', { value }),
      versionStatus: (value: string) => t('versionStatus', { value }),
      action: (value: string) => t('action', { value }),
      command: (value: string) => t('command', { value }),
    }),
    [t],
  );
}

function StatusBadge({ status }: { status: string }) {
  const labels = useWorkflowLabels();
  return <Badge tone={STATUS_TONE[status] ?? 'neutral'} data-status={status}>{labels.status(status)}</Badge>;
}

function DueCell({ row }: { row: WorkflowInstanceSummary }) {
  const t = useTranslations('workflow.inbox');
  const format = useLocaleFormat();
  if (!row.currentStep?.dueAt) return <>{t('noDue')}</>;
  return (
    <>
      {format.dateTime(row.currentStep.dueAt)} {row.currentStep.isOverdue && <Badge tone="danger">{t('overdue')}</Badge>}
    </>
  );
}

/* ======================= inbox ======================= */

export function WorkflowInboxScreen() {
  const t = useTranslations('workflow.inbox');
  const userName = useUserNames();
  const [overdue, setOverdue] = useState(false);
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      <div style={{ marginBlockEnd: 12 }}>
        <Checkbox label={t('overdueOnly')} name="overdue" checked={overdue} onChange={(e) => setOverdue(e.target.checked)} />
      </div>
      <PagedList<WorkflowInstanceSummary>
        queryKey={`wf-inbox|${overdue}`}
        load={(api, q) => api.workflow.inbox({ ...q, overdue: overdue || undefined })}
        caption={t('title')}
        searchLabel={t('requestTitle')}
        emptyText={t('empty')}
        getRowKey={(r) => r.id}
        columns={[
          { key: 'title', header: t('requestTitle'), isRowHeader: true, cell: (r) => <Link href={`/workflow/instances/${r.id}`}>{r.title}</Link> },
          { key: 'process', header: t('process'), cell: (r) => r.definitionName },
          { key: 'step', header: t('step'), cell: (r) => r.currentStep?.name ?? '' },
          { key: 'by', header: <RequesterHeader />, cell: (r) => userName(r.requestedByUserId) },
          { key: 'due', header: t('due'), cell: (r) => <DueCell row={r} /> },
        ]}
      />
    </>
  );
}

function RequesterHeader() {
  const t = useTranslations('workflow.instance');
  return <>{t('requester')}</>;
}

/* ======================= requests ======================= */

export function WorkflowRequestsScreen() {
  const t = useTranslations('workflow.requests');
  const common = useTranslations('common');
  const labels = useWorkflowLabels();
  const format = useLocaleFormat();
  const userName = useUserNames();
  const { can } = useSession();
  const [scope, setScope] = useState<'mine' | 'all'>('mine');
  const [status, setStatus] = useState('');
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      <FormGrid style={{ marginBlockEnd: 12 }}>
        {can('workflow.instances.read') && (
          <SelectField label={t('scope')} name="scope" value={scope} onChange={(e) => setScope(e.target.value === 'all' ? 'all' : 'mine')}
            options={[{ value: 'mine', label: t('mine') }, { value: 'all', label: t('all') }]} />
        )}
        <SelectField label={common('status')} name="status" value={status} placeholder={t('allStatuses')} onChange={(e) => setStatus(e.target.value)}
          options={WORKFLOW_INSTANCE_STATUSES.map((s) => ({ value: s, label: labels.status(s) }))} />
      </FormGrid>
      <PagedList<WorkflowInstanceSummary>
        queryKey={`wf-requests|${scope}|${status}`}
        load={(api, q) => api.workflow.instances.list({ ...q, scope, status: status || undefined })}
        caption={t('title')}
        searchLabel={t('title')}
        emptyText={t('empty')}
        getRowKey={(r) => r.id}
        columns={[
          { key: 'title', header: t('title'), isRowHeader: true, cell: (r) => <Link href={`/workflow/instances/${r.id}`}>{r.title}</Link> },
          { key: 'process', header: <ProcessHeader />, cell: (r) => r.definitionName },
          { key: 'status', header: '', cell: (r) => <StatusBadge status={r.status} /> },
          { key: 'by', header: <RequesterHeader />, cell: (r) => userName(r.requestedByUserId) },
          { key: 'started', header: t('started'), cell: (r) => format.dateTime(r.startedAt) },
        ]}
      />
    </>
  );
}

function ProcessHeader() {
  const t = useTranslations('workflow.inbox');
  return <>{t('process')}</>;
}

/* ======================= instance ======================= */

export function WorkflowInstanceScreen({ id }: { id: string }) {
  const t = useTranslations('workflow.instance');
  const common = useTranslations('common');
  const labels = useWorkflowLabels();
  const format = useLocaleFormat();
  const userName = useUserNames();
  const query = useApiQuery<WorkflowInstance>(`wf-instance|${id}`, (api) => api.workflow.instances.get(id));
  const [command, setCommand] = useState<WorkflowCommand | null>(null);
  const [comment, setComment] = useState('');
  const [done, setDone] = useState<WorkflowCommand | null>(null);
  // One request id per opened dialog: a retried submit (network error, double click) is applied once by the server.
  const [requestId, setRequestId] = useState('');
  const act = useApiMutation((api, i: WorkflowInstance, c: WorkflowCommand) =>
    api.workflow.instances.act(i.id, c, { rowVersion: i.rowVersion, comment: comment.trim() || null, requestId }));
  const errors = useFieldErrors(act.error);
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const i = query.data;
  if (!i) return <Spinner label={common('loading')} />;
  const needsComment = command === 'reject' || command === 'return';
  const actor = (id: string | null) => (id ? userName(id) : t('system'));
  return (
    <>
      <PageHeader
        before={<BackLink href="/workflow/inbox" />}
        title={i.title}
        description={<>{i.definitionName} · {t('version', { n: i.versionNo })}</>}
        actions={i.allowedActions.map((c) => (
          <Button key={c} variant={c === 'approve' || c === 'resubmit' ? 'primary' : c === 'reject' || c === 'cancel' ? 'danger' : 'secondary'}
            data-testid={`wf-${c}`} onClick={() => { act.reset(); setComment(''); setRequestId(newIdempotencyKey('wf')); setCommand(c); }}>
            {labels.command(c)}
          </Button>
        ))}
      />
      {done && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }} data-testid="wf-done">{t('done', { value: done })}</Alert>}
      {i.allowedActions.length === 0 && i.status === 'pending' && <Alert tone="info" style={{ marginBlockEnd: 12 }}>{t('noActions')}</Alert>}
      <div style={{ display: 'grid', gap: 16 }}>
        <Card>
          <DescriptionList>
            <dt>{common('status')}</dt>
            <dd><StatusBadge status={i.status} /></dd>
            <dt>{t('requester')}</dt>
            <dd>{userName(i.requestedByUserId)}</dd>
            <dt>{t('subject')}</dt>
            <dd className="eemp-ltr">{i.subjectModule} / {i.subjectType}</dd>
            {i.currentStep && (
              <>
                <dt>{t('step')}</dt>
                <dd>{t('stepValue', { order: format.number(i.currentStep.order), name: i.currentStep.name })}</dd>
                <dt>{t('permission')}</dt>
                <dd className="eemp-ltr">{i.currentStep.permission}</dd>
                <dt>{t('due')}</dt>
                <dd><DueCell row={i} /></dd>
              </>
            )}
            <dt>{t('started')}</dt>
            <dd>{format.dateTime(i.startedAt)}</dd>
            {i.completedAt && (<><dt>{t('completed')}</dt><dd>{format.dateTime(i.completedAt)}</dd></>)}
          </DescriptionList>
        </Card>
        <Card aria-labelledby="wf-history-h">
          <h2 id="wf-history-h" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('history')}</h2>
          <DataTable
            caption={t('history')}
            rows={i.history}
            getRowKey={(h) => String(h.sequence)}
            columns={[
              { key: 'at', header: t('when'), isRowHeader: true, cell: (h) => format.dateTime(h.occurredAt) },
              { key: 'a', header: '', cell: (h) => labels.action(h.action) },
              { key: 's', header: t('step'), cell: (h) => h.stepName ?? '' },
              { key: 'r', header: t('round'), cell: (h) => format.number(h.round) },
              { key: 'by', header: t('actor'), cell: (h) => actor(h.actorUserId) },
              { key: 'c', header: t('comment'), cell: (h) => h.comment ?? '' },
            ]}
          />
        </Card>
      </div>
      <ConfirmDialog
        open={command !== null}
        tone={command === 'reject' || command === 'cancel' ? 'danger' : 'primary'}
        title={t('confirmTitle', { value: command ?? '' })}
        confirmLabel={labels.command(command ?? '')}
        cancelLabel={common('cancel')}
        busy={act.busy}
        onCancel={() => setCommand(null)}
        onConfirm={async () => {
          if (!command || (needsComment && !comment.trim())) return;
          const r = await act.run(i, command);
          if (r.ok) {
            query.setData(r.value);
            setDone(command);
            setCommand(null);
          }
        }}
      >
        <ErrorAlert error={act.error} onReload={() => { act.reset(); setCommand(null); query.reload(); }} />
        <TextAreaField label={t('comment')} name="comment" required={needsComment} maxLength={2000} value={comment}
          hint={needsComment ? t('commentRequired') : undefined} error={errors.comment} onChange={(e) => setComment(e.target.value)} />
      </ConfirmDialog>
    </>
  );
}

/* ======================= definitions ======================= */

export function WorkflowDefinitionsScreen() {
  const t = useTranslations('workflow.definitions');
  const common = useTranslations('common');
  const format = useLocaleFormat();
  const { can } = useSession();
  const query = useApiQuery<WorkflowDefinitionSummary[]>('wf-defs', (api) => api.workflow.definitions.list());
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={can('workflow.definitions.manage') && <ButtonLink href="/workflow/definitions/new">{t('new')}</ButtonLink>} />
      <DataTable
        caption={t('title')}
        rows={query.data ?? []}
        loading={query.loading}
        loadingLabel={common('loading')}
        getRowKey={(r) => r.id}
        empty={<StatePanel title={t('empty')} />}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (r) => <Link href={`/workflow/definitions/${r.id}`}>{r.name}</Link> },
          { key: 'key', header: t('key'), cell: (r) => <span className="eemp-ltr">{r.key}</span> },
          { key: 'subject', header: t('subjectModule'), cell: (r) => <span className="eemp-ltr">{r.subjectModule} / {r.subjectType}</span> },
          { key: 'pub', header: t('published'), cell: (r) => (r.publishedVersion ? t('versionNo', { n: format.number(r.publishedVersion) }) : t('none')) },
          { key: 'latest', header: t('latest'), cell: (r) => t('versionNo', { n: format.number(r.latestVersion) }) },
          { key: 'active', header: common('status'), cell: (r) => <Badge tone={r.isActive ? 'success' : 'neutral'}>{r.isActive ? common('active') : common('inactive')}</Badge> },
        ]}
      />
    </>
  );
}

const SLUG = /^[a-z][a-z0-9_.-]{1,49}$/;

/** Step editor shared by create and draft editing. Permission codes come from the catalog (roles.read) or are typed. */
function StepsEditor({ steps, onChange, readOnly, errors }: { steps: WorkflowStepInput[]; onChange: (s: WorkflowStepInput[]) => void; readOnly?: boolean; errors: Record<string, string> }) {
  const t = useTranslations('workflow.definitions');
  const roles = useTranslations('roles');
  const { can } = useSession();
  const permissions = useApiQuery<Permission[]>(`wf-perms|${can('roles.read')}`, async (api) => (can('roles.read') ? api.permissions.list() : []));
  const users = useApiQuery(`wf-users|${can('users.read')}`, async (api) => (can('users.read') ? (await api.users.list({ pageSize: 100 })).items : []));
  const set = (i: number, patch: Partial<WorkflowStepInput>) => onChange(steps.map((s, k) => (k === i ? { ...s, ...patch } : s)));
  const move = (i: number, d: -1 | 1) => {
    const next = [...steps];
    const [s] = next.splice(i, 1);
    next.splice(i + d, 0, s!);
    onChange(next);
  };
  const label = (code: string) => {
    const resource = code.slice(0, code.lastIndexOf('.'));
    return `${roles('module', { module: resource.replace(/\./g, '_') })} · ${roles('action', { action: code.slice(code.lastIndexOf('.') + 1) })} (${code})`;
  };
  return (
    <div style={{ display: 'grid', gap: 12 }}>
      {errors.steps && <Alert tone="danger" role="alert">{errors.steps}</Alert>}
      {steps.map((s, i) => (
        <fieldset key={i} style={{ border: '1px solid var(--eemp-color-border)', borderRadius: 8, padding: 12 }} data-testid="wf-step">
          <legend style={{ fontWeight: 600 }}>{t('step', { n: i + 1 })}</legend>
          <FormGrid>
            <TextField label={t('stepName')} name={`steps[${i}].name`} required readOnly={readOnly} value={s.name} error={errors[`steps[${i}].name`]} onChange={(e) => set(i, { name: e.target.value })} />
            {(permissions.data ?? []).length > 0 ? (
              <SelectField label={t('permission')} name={`steps[${i}].permission`} required disabled={readOnly} value={s.permission} placeholder={t('choosePermission')}
                error={errors[`steps[${i}].permission`]} onChange={(e) => set(i, { permission: e.target.value })}
                options={(permissions.data ?? []).filter((p) => /\.(approve|manage|publish)$/.test(p.code)).map((p) => ({ value: p.code, label: label(p.code) }))} />
            ) : (
              <TextField label={t('permission')} name={`steps[${i}].permission`} dir="ltr" required readOnly={readOnly} value={s.permission}
                error={errors[`steps[${i}].permission`]} onChange={(e) => set(i, { permission: e.target.value.trim() })} />
            )}
            {(users.data ?? []).length > 0 && (
              <SelectField label={t('assignee')} name={`steps[${i}].assigneeUserId`} disabled={readOnly} value={s.assigneeUserId ?? ''} placeholder={t('anyHolder')} hint={t('assigneeHint')}
                error={errors[`steps[${i}].assigneeUserId`]} onChange={(e) => set(i, { assigneeUserId: e.target.value || null })}
                options={(users.data ?? []).map((u) => ({ value: u.id, label: `${u.displayName} (${u.userName})` }))} />
            )}
            <TextField label={t('sla')} name={`steps[${i}].slaHours`} type="number" min={1} max={8760} dir="ltr" readOnly={readOnly} value={s.slaHours ?? ''}
              error={errors[`steps[${i}].slaHours`]} onChange={(e) => set(i, { slaHours: e.target.value === '' ? null : Number(e.target.value) })} />
          </FormGrid>
          <div style={{ marginBlockStart: 8 }}>
            <Checkbox label={t('allowReturn')} disabled={readOnly} checked={s.allowReturn} onChange={(e) => set(i, { allowReturn: e.target.checked })} />
          </div>
          {!readOnly && (
            <div style={{ display: 'flex', gap: 4, marginBlockStart: 8, flexWrap: 'wrap' }}>
              <Button type="button" size="sm" variant="ghost" disabled={i === 0} onClick={() => move(i, -1)}>{t('moveUp')}</Button>
              <Button type="button" size="sm" variant="ghost" disabled={i === steps.length - 1} onClick={() => move(i, 1)}>{t('moveDown')}</Button>
              <Button type="button" size="sm" variant="ghost" disabled={steps.length === 1} onClick={() => onChange(steps.filter((_, k) => k !== i))}>{t('removeStep')}</Button>
            </div>
          )}
        </fieldset>
      ))}
      {!readOnly && steps.length < 10 && (
        <div><Button type="button" variant="secondary" onClick={() => onChange([...steps, { name: '', permission: '', assigneeUserId: null, slaHours: null, allowReturn: true }])}>{t('addStep')}</Button></div>
      )}
    </div>
  );
}

const toInput = (v: WorkflowVersion): WorkflowStepInput[] =>
  v.steps.map((s) => ({ name: s.name, permission: s.permission, assigneeUserId: s.assigneeUserId, slaHours: s.slaHours, allowReturn: s.allowReturn }));

export function WorkflowDefinitionCreateScreen() {
  const t = useTranslations('workflow.definitions');
  const common = useTranslations('common');
  const router = useRouter();
  const [v, setV] = useState({ key: '', name: '', subjectModule: '', subjectType: '', requireDistinctApprovers: true });
  const [steps, setSteps] = useState<WorkflowStepInput[]>([{ name: '', permission: '', assigneeUserId: null, slaHours: null, allowReturn: true }]);
  const create = useApiMutation((api) => api.workflow.definitions.create({ ...v, steps }));
  const errors = useFieldErrors(create.error);
  const slugError = (value: string) => (value && !SLUG.test(value) ? t('keyHint') : undefined);
  return (
    <>
      <PageHeader before={<BackLink href="/workflow/definitions" />} title={t('createTitle')} />
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          const r = await create.run();
          if (r.ok) router.push(`/workflow/definitions/${r.value.id}?notice=created`);
        }}
        style={{ display: 'grid', gap: 16 }}
      >
        <ErrorAlert error={create.error} />
        <Card>
          <FormGrid>
            <TextField label={t('key')} name="key" dir="ltr" required value={v.key} hint={t('keyHint')} error={errors.key ?? slugError(v.key)} onChange={(e) => setV({ ...v, key: e.target.value.trim() })} />
            <TextField label={t('name')} name="name" required value={v.name} error={errors.name} onChange={(e) => setV({ ...v, name: e.target.value })} />
            <TextField label={t('subjectModule')} name="subjectModule" dir="ltr" required value={v.subjectModule} hint={t('subjectHint')} error={errors.subjectModule ?? slugError(v.subjectModule)}
              onChange={(e) => setV({ ...v, subjectModule: e.target.value.trim() })} />
            <TextField label={t('subjectType')} name="subjectType" dir="ltr" required value={v.subjectType} error={errors.subjectType ?? slugError(v.subjectType)}
              onChange={(e) => setV({ ...v, subjectType: e.target.value.trim() })} />
          </FormGrid>
          <div style={{ marginBlockStart: 12 }}>
            <Checkbox label={t('distinct')} checked={v.requireDistinctApprovers} onChange={(e) => setV({ ...v, requireDistinctApprovers: e.target.checked })} />
          </div>
        </Card>
        <Card aria-labelledby="steps-h">
          <h2 id="steps-h" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('steps')}</h2>
          <StepsEditor steps={steps} onChange={setSteps} errors={errors} />
        </Card>
        <div style={{ display: 'flex', gap: 8 }}>
          <Button type="submit" loading={create.busy}>{common('create')}</Button>
          <ButtonLink href="/workflow/definitions" variant="secondary">{common('cancel')}</ButtonLink>
        </div>
      </form>
    </>
  );
}

export function WorkflowDefinitionScreen({ id, notice }: { id: string; notice?: string }) {
  const t = useTranslations('workflow.definitions');
  const common = useTranslations('common');
  const labels = useWorkflowLabels();
  const format = useLocaleFormat();
  const { can } = useSession();
  const manage = can('workflow.definitions.manage');
  const query = useApiQuery<WorkflowDefinition>(`wf-def|${id}`, (api) => api.workflow.definitions.get(id));
  const def = query.data;
  const [versionNo, setVersionNo] = useState<number | null>(null);
  const version = def?.versions.find((x) => x.versionNo === (versionNo ?? def.latestVersion)) ?? def?.versions.at(-1);
  const [steps, setSteps] = useState<WorkflowStepInput[]>([]);
  const [distinct, setDistinct] = useState(true);
  const [meta, setMeta] = useState({ name: '', isActive: true });
  const [message, setMessage] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);
  useEffect(() => {
    if (version) {
      setSteps(toInput(version));
      setDistinct(version.requireDistinctApprovers);
    }
  }, [version]);
  useEffect(() => {
    if (def) setMeta({ name: def.name, isActive: def.isActive });
  }, [def]);
  const saveMeta = useApiMutation((api, d: WorkflowDefinition) => api.workflow.definitions.update(d.id, { ...meta, rowVersion: d.rowVersion }));
  const saveVersion = useApiMutation((api, d: WorkflowDefinition, v: WorkflowVersion) =>
    api.workflow.definitions.updateVersion(d.id, v.versionNo, { requireDistinctApprovers: distinct, steps, rowVersion: v.rowVersion }));
  const newVersion = useApiMutation((api, d: WorkflowDefinition) => api.workflow.definitions.newVersion(d.id));
  const publish = useApiMutation((api, d: WorkflowDefinition, v: WorkflowVersion) => api.workflow.definitions.publish(d.id, v.versionNo, d.rowVersion));
  const errors = useFieldErrors(saveVersion.error ?? saveMeta.error);
  if (query.error && !def) return <LoadError error={query.error} onRetry={query.reload} />;
  if (!def || !version) return <Spinner label={common('loading')} />;
  const editable = manage && version.status === 'draft';
  const apply = (d: WorkflowDefinition, text: string) => {
    query.setData(d);
    setMessage(text);
  };
  const anyError = saveMeta.error ?? saveVersion.error ?? newVersion.error ?? publish.error;
  return (
    <>
      <PageHeader
        before={<BackLink href="/workflow/definitions" />}
        title={def.name}
        description={<span className="eemp-ltr">{def.key} · {def.subjectModule} / {def.subjectType}</span>}
        actions={manage && !def.versions.some((x) => x.status === 'draft') && (
          <Button variant="secondary" loading={newVersion.busy}
            onClick={async () => {
              const r = await newVersion.run(def);
              if (r.ok) {
                setVersionNo(r.value.latestVersion);
                apply(r.value, t('versionSaved'));
              }
            }}>{t('newVersion')}</Button>
        )}
      />
      <Notice notice={notice} />
      {message && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{message}</Alert>}
      <ErrorAlert error={anyError} onReload={() => { saveMeta.reset(); saveVersion.reset(); newVersion.reset(); publish.reset(); query.reload(); }} />
      <div style={{ display: 'grid', gap: 16 }}>
        <Card>
          <form
            onSubmit={async (e) => {
              e.preventDefault();
              const r = await saveMeta.run(def);
              if (r.ok) apply(r.value, t('settingsSaved'));
            }}
          >
            <FormGrid>
              <TextField label={t('name')} name="name" required readOnly={!manage} value={meta.name} error={errors.name} onChange={(e) => setMeta({ ...meta, name: e.target.value })} />
              <div style={{ alignSelf: 'end' }}>
                <Checkbox label={t('active')} disabled={!manage} checked={meta.isActive} onChange={(e) => setMeta({ ...meta, isActive: e.target.checked })} />
              </div>
            </FormGrid>
            {manage && <div style={{ marginBlockStart: 12 }}><Button type="submit" variant="secondary" loading={saveMeta.busy}>{common('save')}</Button></div>}
          </form>
        </Card>
        <Card aria-labelledby="versions-h">
          <h2 id="versions-h" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('versions')}</h2>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBlockEnd: 12 }} role="group" aria-label={t('versions')}>
            {def.versions.map((x) => (
              <Button key={x.versionNo} size="sm" variant={x.versionNo === version.versionNo ? 'primary' : 'ghost'} aria-pressed={x.versionNo === version.versionNo}
                onClick={() => setVersionNo(x.versionNo)} data-testid={`wf-version-${x.versionNo}`}>
                {t('versionNo', { n: format.number(x.versionNo) })} · {labels.versionStatus(x.status)}
              </Button>
            ))}
          </div>
          {!editable && <p style={{ fontSize: '0.875rem' }}>{t('readOnlyVersion', { n: format.number(version.versionNo), status: labels.versionStatus(version.status) })}</p>}
          <div style={{ marginBlockEnd: 12 }}>
            <Checkbox label={t('distinct')} disabled={!editable} checked={distinct} onChange={(e) => setDistinct(e.target.checked)} />
          </div>
          <StepsEditor steps={steps} onChange={setSteps} readOnly={!editable} errors={errors} />
          {editable && (
            <div style={{ display: 'flex', gap: 8, marginBlockStart: 16, flexWrap: 'wrap' }}>
              <Button variant="secondary" loading={saveVersion.busy}
                onClick={async () => {
                  const r = await saveVersion.run(def, version);
                  if (r.ok) apply(r.value, t('versionSaved'));
                }}>{t('saveVersion')}</Button>
              <Button onClick={() => { publish.reset(); setPublishing(true); }}>{t('publish')}</Button>
            </div>
          )}
        </Card>
      </div>
      <ConfirmDialog
        open={publishing}
        tone="primary"
        title={t('publishTitle', { n: format.number(version.versionNo) })}
        description={t('publishBody')}
        confirmLabel={t('publish')}
        cancelLabel={common('cancel')}
        busy={publish.busy}
        onCancel={() => setPublishing(false)}
        onConfirm={async () => {
          const r = await publish.run(def, version);
          setPublishing(false);
          if (r.ok) apply(r.value, t('published_notice', { n: format.number(version.versionNo) }));
        }}
      >
        <ErrorAlert error={publish.error} />
      </ConfirmDialog>
    </>
  );
}
