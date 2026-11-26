/**
 * Mock of the EEMP-011 workflow engine API (development and tests only; NOT a security boundary).
 * Versioned definitions (draft → published → retired), sequential permission-based steps, inbox with maker-checker
 * and distinct-approver rules, instance detail with history and allowed actions, idempotent commands (requestId).
 * There is no HTTP start endpoint (as in the real API): instances come from the seed.
 */
import type {
  WorkflowCommand,
  WorkflowDefinition,
  WorkflowHistoryItem,
  WorkflowInstance,
  WorkflowInstanceStatus,
  WorkflowStep,
} from '../workflow-types';
import { assertValid, HttpError, json, paginate, uuid } from './http';
import type { MockTenantData, MockUser } from './seed';

export interface MockWorkflow {
  definitions: WorkflowDefinition[];
  instances: (Omit<WorkflowInstance, 'allowedActions'> & { requestIds: string[] })[];
}

export const WORKFLOW_IDS = {
  defDiscount: '90000000-0000-4000-8000-000000000001',
  defReview: '90000000-0000-4000-8000-000000000002',
  instanceSample: '90000000-0000-4000-8000-000000000011',
  instanceMine: '90000000-0000-4000-8000-000000000012',
} as const;

export const emptyWorkflow = (): MockWorkflow => ({ definitions: [], instances: [] });

export function createWorkflowSeed(rv: () => string, ids: { branchNorth: string; userFinance: string; userDemoAdmin: string }): MockWorkflow {
  const started = '2026-11-20T06:30:00Z';
  const step = (name: string, permission: string, slaHours: number | null): WorkflowStep => ({ order: 1, name, permission, assigneeUserId: null, slaHours, allowReturn: true });
  const definitions: WorkflowDefinition[] = [
    {
      id: WORKFLOW_IDS.defDiscount, key: 'discount-approval', name: 'Discount approval', subjectModule: 'finance', subjectType: 'discount', isActive: true,
      publishedVersion: 1, latestVersion: 1, rowVersion: rv(),
      versions: [{ versionNo: 1, status: 'published', requireDistinctApprovers: true, publishedAt: started, rowVersion: rv(), steps: [step('Branch manager approval', 'workflow.tasks.approve', 48)] }],
    },
    {
      id: WORKFLOW_IDS.defReview, key: 'sample-review', name: 'Sample review', subjectModule: 'demo', subjectType: 'document', isActive: true,
      publishedVersion: 1, latestVersion: 1, rowVersion: rv(),
      versions: [{ versionNo: 1, status: 'published', requireDistinctApprovers: true, publishedAt: started, rowVersion: rv(), steps: [step('Review', 'workflow.tasks.approve', 24)] }],
    },
  ];
  const history = (actor: string): WorkflowHistoryItem[] => [
    { sequence: 1, round: 1, action: 'started', fromStatus: null, toStatus: 'pending', stepOrder: 1, stepName: 'Review', actorUserId: actor, comment: null, occurredAt: started },
  ];
  const instance = (id: string, title: string, requestedBy: string) => ({
    id, definitionId: WORKFLOW_IDS.defReview, definitionKey: 'sample-review', definitionName: 'Sample review', versionNo: 1, subjectModule: 'demo',
    subjectType: 'document', subjectId: uuid(), branchId: ids.branchNorth, title, requestedByUserId: requestedBy, status: 'pending' as WorkflowInstanceStatus,
    currentStep: { order: 1, name: 'Review', permission: 'workflow.tasks.approve', assigneeUserId: null, dueAt: '2026-11-21T06:30:00Z', isOverdue: true },
    startedAt: started, completedAt: null, rowVersion: rv(), round: 1, history: history(requestedBy), requestIds: [] as string[],
  });
  return {
    definitions,
    instances: [instance(WORKFLOW_IDS.instanceSample, 'Sample request', ids.userFinance), instance(WORKFLOW_IDS.instanceMine, 'My sample request', ids.userDemoAdmin)],
  };
}

export interface WorkflowContext {
  user: MockUser;
  data: MockTenantData;
  scope: (permission: string) => Set<string> | null;
  now: () => number;
  nextVersion: () => string;
  /** Catalog codes (steps must name an existing permission). */
  permissionCodes: string[];
}

const SLUG = /^[a-z][a-z0-9_.-]{1,49}$/;
const notFound = () => new HttpError(404, 'resource.not_found');
const includes = (scope: Set<string> | null, branchId: string | null) => scope === null || (branchId !== null && scope.has(branchId));
const str = (v: unknown) => (typeof v === 'string' ? v.trim() : '');

function checkVersion(current: string, body: Record<string, unknown>) {
  if (typeof body.rowVersion !== 'string' || body.rowVersion === '') {
    throw new HttpError(400, 'validation.failed', { errors: { rowVersion: ['validation.required'] } });
  }
  if (body.rowVersion !== current) throw new HttpError(409, 'concurrency.conflict');
}

export function workflowRoutes(ctx: WorkflowContext, seg: string[], method: string, url: URL, body: Record<string, unknown>): Response | null {
  if (seg[0] !== 'workflow') return null;
  const { user } = ctx;
  const wf = ctx.data.workflow;
  const nv = ctx.nextVersion;
  const nowIso = () => new Date(ctx.now()).toISOString();
  const demand = (permission: string) => {
    const s = ctx.scope(permission);
    if (s !== null && s.size === 0) throw new HttpError(403, 'authorization.forbidden');
    return s;
  };
  const summary = (d: WorkflowDefinition) => {
    const { versions: _v, ...rest } = d;
    return rest;
  };
  const steps = (raw: unknown) => {
    const list = Array.isArray(raw) ? (raw as Record<string, unknown>[]) : [];
    const errors: Record<string, string[]> = {};
    if (list.length === 0 || list.length > 10) errors.steps = ['validation.required'];
    const result = list.map<WorkflowStep>((s, i) => {
      if (!str(s.name)) errors[`steps[${i}].name`] = ['validation.required'];
      if (!str(s.permission)) errors[`steps[${i}].permission`] = ['validation.required'];
      else if (!ctx.permissionCodes.includes(str(s.permission))) errors[`steps[${i}].permission`] = ['validation.unknown_permission'];
      const sla = typeof s.slaHours === 'number' ? s.slaHours : null;
      if (sla !== null && (sla < 1 || sla > 8760 || !Number.isInteger(sla))) errors[`steps[${i}].slaHours`] = ['validation.invalid_value'];
      const assignee = typeof s.assigneeUserId === 'string' && s.assigneeUserId ? s.assigneeUserId : null;
      return { order: i + 1, name: str(s.name), permission: str(s.permission), assigneeUserId: assignee, slaHours: sla, allowReturn: s.allowReturn !== false };
    });
    return { result, errors };
  };

  /* ----- definitions ----- */
  if (seg[1] === 'definitions') {
    if (seg.length === 2 && method === 'GET') {
      demand('workflow.definitions.read');
      return json(200, wf.definitions.map(summary));
    }
    if (seg.length === 2 && method === 'POST') {
      demand('workflow.definitions.manage');
      const { result, errors } = steps(body.steps);
      for (const f of ['key', 'subjectModule', 'subjectType']) {
        if (!str(body[f])) errors[f] = ['validation.required'];
        else if (!SLUG.test(str(body[f]))) errors[f] = ['validation.invalid_slug'];
      }
      if (!str(body.name)) errors.name = ['validation.required'];
      assertValid(errors);
      if (wf.definitions.some((d) => d.key === str(body.key))) throw new HttpError(409, 'workflow.definition_key_taken');
      const d: WorkflowDefinition = {
        id: uuid(), key: str(body.key), name: str(body.name), subjectModule: str(body.subjectModule), subjectType: str(body.subjectType), isActive: true,
        publishedVersion: null, latestVersion: 1, rowVersion: nv(),
        versions: [{ versionNo: 1, status: 'draft', requireDistinctApprovers: body.requireDistinctApprovers !== false, publishedAt: null, rowVersion: nv(), steps: result }],
      };
      wf.definitions.push(d);
      return json(201, d);
    }
    const d = wf.definitions.find((x) => x.id === seg[2]);
    if (!d) throw notFound();
    if (seg.length === 3 && method === 'GET') {
      demand('workflow.definitions.read');
      return json(200, d);
    }
    demand('workflow.definitions.manage');
    if (seg.length === 3 && method === 'PUT') {
      checkVersion(d.rowVersion, body);
      if (!str(body.name)) throw new HttpError(400, 'validation.failed', { errors: { name: ['validation.required'] } });
      Object.assign(d, { name: str(body.name), isActive: body.isActive !== false, rowVersion: nv() });
      return json(200, d);
    }
    if (seg[3] === 'versions' && seg.length === 4 && method === 'POST') {
      if (d.versions.some((v) => v.status === 'draft')) throw new HttpError(409, 'workflow.draft_exists');
      const latest = d.versions.at(-1)!;
      d.versions.push({ ...latest, versionNo: latest.versionNo + 1, status: 'draft', publishedAt: null, rowVersion: nv(), steps: latest.steps.map((s) => ({ ...s })) });
      d.latestVersion = latest.versionNo + 1;
      d.rowVersion = nv();
      return json(201, d);
    }
    const v = d.versions.find((x) => x.versionNo === Number(seg[4]));
    if (seg[3] === 'versions' && !v) throw notFound();
    if (v && seg.length === 5 && method === 'PUT') {
      checkVersion(v.rowVersion, body);
      if (v.status !== 'draft') throw new HttpError(409, 'workflow.version_not_draft');
      const { result, errors } = steps(body.steps);
      assertValid(errors);
      Object.assign(v, { steps: result, requireDistinctApprovers: body.requireDistinctApprovers !== false, rowVersion: nv() });
      d.rowVersion = nv();
      return json(200, d);
    }
    if (v && seg[5] === 'publish' && method === 'POST') {
      checkVersion(d.rowVersion, body);
      if (v.status !== 'draft') throw new HttpError(409, 'workflow.version_not_draft');
      d.versions.forEach((x) => {
        if (x.status === 'published') x.status = 'retired';
      });
      Object.assign(v, { status: 'published', publishedAt: nowIso(), rowVersion: nv() });
      Object.assign(d, { publishedVersion: v.versionNo, rowVersion: nv() });
      return json(200, d);
    }
    throw notFound();
  }

  /* ----- instances ----- */
  type Inst = MockWorkflow['instances'][number];
  const definitionOf = (i: Inst) => wf.definitions.find((d) => d.id === i.definitionId)!;
  const stepsOf = (i: Inst) => definitionOf(i).versions.find((v) => v.versionNo === i.versionNo)!;
  const approvedThisRound = (i: Inst) => i.history.filter((h) => h.round === i.round && h.action === 'approved').map((h) => h.actorUserId);
  const eligible = (i: Inst) => {
    if (i.status !== 'pending' || !i.currentStep) return false;
    if (i.requestedByUserId === user.id) return false;
    if (i.currentStep.assigneeUserId && i.currentStep.assigneeUserId !== user.id) return false;
    if (!includes(ctx.scope(i.currentStep.permission), i.branchId)) return false;
    if (stepsOf(i).requireDistinctApprovers && approvedThisRound(i).includes(user.id)) return false;
    return true;
  };
  const canManage = (i: Inst) => includes(ctx.scope('workflow.instances.manage'), i.branchId) && (ctx.scope('workflow.instances.manage')?.size ?? 1) > 0;
  const allowed = (i: Inst): WorkflowCommand[] => {
    const out: WorkflowCommand[] = [];
    if (eligible(i)) out.push('approve', 'reject', ...(i.currentStep && stepsOf(i).steps[i.currentStep.order - 1]?.allowReturn ? (['return'] as const) : []));
    if (i.status === 'returned' && i.requestedByUserId === user.id) out.push('resubmit');
    if ((i.status === 'pending' || i.status === 'returned') && (i.requestedByUserId === user.id || canManage(i))) out.push('cancel');
    return out;
  };
  const visible = (i: Inst) =>
    i.requestedByUserId === user.id || eligible(i) || i.history.some((h) => h.actorUserId === user.id) || includes(ctx.scope('workflow.instances.read'), i.branchId) && (ctx.scope('workflow.instances.read')?.size ?? 1) > 0;
  const dto = (i: Inst): WorkflowInstance => {
    const { requestIds: _r, ...rest } = i;
    if (rest.currentStep?.dueAt) rest.currentStep.isOverdue = rest.currentStep.dueAt < nowIso();
    return { ...rest, allowedActions: allowed(i) };
  };
  const sum = (i: Inst) => {
    const { history: _h, round: _r, allowedActions: _a, ...rest } = dto(i);
    return rest;
  };

  if (seg[1] === 'inbox' && method === 'GET') {
    const p = url.searchParams;
    const rows = wf.instances
      .filter(eligible)
      .filter((i) => (!p.get('definitionKey') || i.definitionKey === p.get('definitionKey')) && (p.get('overdue') !== 'true' || !!i.currentStep?.isOverdue))
      .sort((a, b) => (a.currentStep?.dueAt ?? '~').localeCompare(b.currentStep?.dueAt ?? '~'))
      .map(sum);
    return json(200, paginate(rows, url, (r) => [r.title, r.definitionName]));
  }
  if (seg[1] === 'instances' && seg.length === 2 && method === 'GET') {
    const p = url.searchParams;
    const all = p.get('scope') === 'all';
    const scope = all ? demand('workflow.instances.read') : null;
    const rows = wf.instances
      .filter((i) => (all ? includes(scope, i.branchId) : i.requestedByUserId === user.id))
      .filter((i) => (!p.get('status') || i.status === p.get('status')) && (!p.get('definitionKey') || i.definitionKey === p.get('definitionKey')))
      .sort((a, b) => b.startedAt.localeCompare(a.startedAt))
      .map(sum);
    return json(200, paginate(rows, url, (r) => [r.title, r.definitionName]));
  }
  if (seg[1] === 'instances' && seg[2]) {
    const i = wf.instances.find((x) => x.id === seg[2]);
    if (!i || !visible(i)) throw notFound();
    if (seg.length === 3 && method === 'GET') return json(200, dto(i));
    const command = seg[3] as WorkflowCommand;
    if (method !== 'POST' || !['approve', 'reject', 'return', 'resubmit', 'cancel'].includes(command)) throw notFound();
    const requestId = str(body.requestId);
    if (requestId && i.requestIds.includes(requestId)) return json(200, dto(i));
    checkVersion(i.rowVersion, body);
    const comment = str(body.comment) || null;
    if ((command === 'reject' || command === 'return') && !comment) throw new HttpError(400, 'validation.failed', { errors: { comment: ['validation.required'] } });
    const terminal = ['approved', 'rejected', 'cancelled'].includes(i.status);
    const invalid = () => new HttpError(409, 'workflow.invalid_transition', { params: { from: i.status, action: command } });
    if (terminal) throw invalid();
    if (command === 'approve' || command === 'reject' || command === 'return') {
      if (i.status !== 'pending') throw invalid();
      if (i.requestedByUserId === user.id) throw new HttpError(403, 'workflow.self_approval_forbidden');
      if (stepsOf(i).requireDistinctApprovers && approvedThisRound(i).includes(user.id)) throw new HttpError(403, 'workflow.approver_already_acted');
      if (!eligible(i)) throw new HttpError(403, 'workflow.not_eligible');
      if (command === 'return' && !stepsOf(i).steps[i.currentStep!.order - 1]?.allowReturn) throw new HttpError(422, 'workflow.return_not_allowed');
    }
    if (command === 'resubmit' && (i.status !== 'returned' || i.requestedByUserId !== user.id)) throw i.status !== 'returned' ? invalid() : new HttpError(403, 'authorization.forbidden');
    if (command === 'cancel' && !(i.requestedByUserId === user.id || canManage(i))) throw new HttpError(403, 'authorization.forbidden');
    const from = i.status;
    const stepOrder = i.currentStep?.order ?? null;
    const stepName = i.currentStep?.name ?? null;
    const version = stepsOf(i);
    const enter = (order: number) => {
      const s = version.steps[order - 1]!;
      i.currentStep = {
        order, name: s.name, permission: s.permission, assigneeUserId: s.assigneeUserId,
        dueAt: s.slaHours ? new Date(ctx.now() + s.slaHours * 3_600_000).toISOString() : null, isOverdue: false,
      };
    };
    let action: WorkflowHistoryItem['action'];
    if (command === 'approve') {
      action = 'approved';
      if (stepOrder! < version.steps.length) enter(stepOrder! + 1);
      else Object.assign(i, { status: 'approved', currentStep: null, completedAt: nowIso() });
    } else if (command === 'reject') {
      action = 'rejected';
      Object.assign(i, { status: 'rejected', currentStep: null, completedAt: nowIso() });
    } else if (command === 'return') {
      action = 'returned';
      Object.assign(i, { status: 'returned', currentStep: null });
    } else if (command === 'resubmit') {
      action = 'resubmitted';
      i.status = 'pending';
      i.round += 1;
      enter(1);
    } else {
      action = 'cancelled';
      Object.assign(i, { status: 'cancelled', currentStep: null, completedAt: nowIso() });
    }
    i.history.push({ sequence: i.history.length + 1, round: i.round, action, fromStatus: from, toStatus: i.status, stepOrder, stepName, actorUserId: user.id, comment, occurredAt: nowIso() });
    if (requestId) i.requestIds.push(requestId);
    i.rowVersion = nv();
    return json(200, dto(i));
  }
  throw notFound();
}
