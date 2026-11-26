import { beforeEach, describe, expect, it } from 'vitest';

import { FINANCE_IDS } from '../src/mock/finance';
import { WORKFLOW_IDS } from '../src/mock/workflow';
import { createMockBackend, DEFAULT_MOCK_PASSWORD, MOCK_API_BASE_URL, MOCK_IDS } from '../src/mock';
import {
  authorityFromRedirect, createApiClient, createEndpoints, newIdempotencyKey, parseAmount,
  type ApprovalItem, type CurrentUser, type Invoice, type InvoiceDiscount, type Payment, type PaymentStatusResult, type PayOnlineResult, type StudentAccount, type TokenResponse,
  type WorkflowInstance,
} from '../src';

/** Fixed clock: 2026-11-25 in Tehran. Seed: INV-000001 (ST-1001) 12,000,000 IRR in two installments, R-000001 5,000,000 cash. */
const NOW = Date.parse('2026-11-25T08:00:00Z');
let backend: ReturnType<typeof createMockBackend>;

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const call = async <T = any>(method: string, path: string, token: string | null, body?: unknown) => {
  const response = await backend.fetch(`${MOCK_API_BASE_URL}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  const type = response.headers.get('content-type') ?? '';
  return { status: response.status, body: (text && type.includes('json') ? JSON.parse(text) : text) as T, headers: response.headers };
};

const login = async (userName: string) => {
  const r = await backend.fetch(`${MOCK_API_BASE_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tenantSlug: 'demo', userName, password: DEFAULT_MOCK_PASSWORD }),
  });
  expect(r.status).toBe(200);
  return ((await r.json()) as TokenResponse).accessToken;
};

beforeEach(() => {
  backend = createMockBackend({ now: () => NOW });
});

describe('mock finance: permissions', () => {
  it('finance.officer holds every finance code; tenant admin holds none (security baseline #1)', async () => {
    const officer = await call<CurrentUser>('GET', '/auth/me', await login('finance.officer'));
    expect(officer.body.permissions).toEqual(expect.arrayContaining(['finance.read', 'finance.approve', 'finance.reports.read', 'finance.setup.manage']));
    const admin = await call<CurrentUser>('GET', '/auth/me', await login('demo.admin'));
    expect(admin.body.permissions.some((p) => p.startsWith('finance.'))).toBe(false);
    expect(admin.body.permissions).toEqual(expect.arrayContaining(['workflow.definitions.manage', 'workflow.tasks.approve']));
    expect((await call('GET', '/finance/invoices', await login('demo.admin'))).status).toBe(403);
  });
});

describe('mock finance: invoices, payments and receipts', () => {
  it('records a cash payment with a gap-free receipt number and allocates it to the oldest installment', async () => {
    const officer = await login('finance.officer');
    const before = await call<Invoice>('GET', `/finance/invoices/${FINANCE_IDS.invoice1}`, officer);
    expect(before.body.paidAmount).toBe(5_000_000);
    expect(before.body.status).toBe('partiallyPaid');
    const key = newIdempotencyKey('pay');
    const body = { studentId: MOCK_IDS.student1, method: 'cash', amount: 2_000_000, idempotencyKey: key };
    const paid = await call<Payment>('POST', '/finance/payments', officer, body);
    expect(paid.status).toBe(201);
    expect(paid.body.receiptNumber).toBe('R-000002');
    expect(paid.body.allocations.map((a) => a.amount)).toEqual([1_000_000, 1_000_000]);
    // Same idempotency key: the same payment, no new receipt.
    const replay = await call<Payment>('POST', '/finance/payments', officer, body);
    expect(replay.status).toBe(200);
    expect(replay.body.id).toBe(paid.body.id);
    const account = await call<StudentAccount>('GET', `/finance/students/${MOCK_IDS.student1}/account`, officer);
    expect(account.body.balance).toBe(5_000_000);
    const pdf = await call('GET', `/finance/payments/${paid.body.id}/receipt/pdf?lang=fa-IR`, officer);
    expect(pdf.headers.get('content-type')).toBe('application/pdf');
    expect(pdf.headers.get('content-disposition')).toContain('receipt-R-000002.pdf');
  });

  it('cheques stay pending (no receipt) until cleared', async () => {
    const officer = await login('finance.officer');
    const cheque = await call<Payment>('POST', '/finance/payments', officer, {
      studentId: MOCK_IDS.student1, method: 'cheque', amount: 1_000_000, idempotencyKey: newIdempotencyKey(),
      cheque: { number: '123456', bank: 'Melli', dueDate: '2026-12-01' },
    });
    expect(cheque.body.status).toBe('pending');
    expect(cheque.body.receiptNumber).toBeNull();
    expect((await call('GET', `/finance/payments/${cheque.body.id}/receipt/pdf`, officer)).body.code).toBe('finance.receipt_unavailable');
    const cleared = await call<Payment>('POST', `/finance/payments/${cheque.body.id}/cheque-status`, officer, { rowVersion: cheque.body.rowVersion, status: 'cleared' });
    expect(cleared.body.status).toBe('succeeded');
    expect(cleared.body.receiptNumber).toBe('R-000002');
  });

  it('draft invoice → discount maker-checker → issue with a number; drafts are hidden from the portal', async () => {
    const maker = await login('finance.officer');
    const draft = await call<Invoice>('POST', '/finance/invoices', maker, {
      studentId: MOCK_IDS.student2, lines: [{ feeItemId: FINANCE_IDS.feeItemTuition, quantity: 1 }],
    });
    expect(draft.status).toBe(201);
    expect(draft.body.number).toBeNull();
    const discount = await call<InvoiceDiscount>('POST', `/finance/invoices/${draft.body.id}/discounts`, maker, { kind: 'scholarship', type: 'percent', value: 25, reason: 'Merit' });
    expect(discount.body.status).toBe('pending');
    const blocked = await call('POST', `/finance/invoices/${draft.body.id}/issue`, maker, { rowVersion: (await call<Invoice>('GET', `/finance/invoices/${draft.body.id}`, maker)).body.rowVersion });
    expect(blocked.body.code).toBe('finance.discount_pending');
    const self = await call('POST', `/finance/discounts/${discount.body.id}/approve`, maker, { rowVersion: discount.body.rowVersion });
    expect(self.status).toBe(403);
    expect(self.body.code).toBe('finance.maker_checker');
    const checker = await login('finance.checker');
    const queue = await call<ApprovalItem[]>('GET', '/finance/approvals?status=pending', checker);
    expect(queue.body.find((a) => a.id === discount.body.id)?.amount).toBe(3_000_000);
    expect((await call('POST', `/finance/discounts/${discount.body.id}/approve`, checker, { rowVersion: discount.body.rowVersion })).status).toBe(200);
    const updated = await call<Invoice>('GET', `/finance/invoices/${draft.body.id}`, maker);
    expect(updated.body.total).toBe(9_000_000);
    const guardian = await login('guardian.ali');
    expect((await call('GET', `/portal/me/finance/invoices/${draft.body.id}`, guardian)).status).toBe(404);
    const issued = await call<Invoice>('POST', `/finance/invoices/${draft.body.id}/issue`, maker, { rowVersion: updated.body.rowVersion });
    expect(issued.body.number).toBe('INV-000002');
    expect((await call('GET', `/portal/me/finance/invoices/${draft.body.id}`, guardian)).status).toBe(200);
  });

  it('bulk generation is idempotent per enrollment and fee item', async () => {
    const officer = await login('finance.officer');
    const body = { classId: 'e0000000-0000-4000-8000-000000000051', feeItemIds: [FINANCE_IDS.feeItemTuition] };
    const first = await call('POST', '/finance/invoices/generate-bulk', officer, body);
    expect(first.body).toMatchObject({ created: 1, skipped: 1 }); // ST-1001 already has the tuition charge
    const again = await call('POST', '/finance/invoices/generate-bulk', officer, body);
    expect(again.body).toMatchObject({ created: 0, skipped: 2 });
  });

  it('reports return rows and CSV with a BOM', async () => {
    const officer = await login('finance.officer');
    const outstanding = await call('GET', '/finance/reports/outstanding', officer);
    expect(outstanding.body[0]).toMatchObject({ number: 'INV-000001', balance: 7_000_000 });
    const csv = await backend.fetch(`${MOCK_API_BASE_URL}/finance/reports/collections?format=csv`, { headers: { Authorization: `Bearer ${officer}` } });
    expect(csv.headers.get('content-type')).toContain('text/csv');
    expect([...new Uint8Array(await csv.arrayBuffer()).slice(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
    const aging = await call('GET', '/finance/reports/aging?asOf=2026-11-25', officer);
    expect(aging.body.buckets.d31_60).toBe(1_000_000);
  });
});

describe('mock finance: portal online payment (fake gateway)', () => {
  it('guardian pays the invoice balance; the anonymous callback settles it once', async () => {
    const guardian = await login('guardian.ali');
    const start = await call<PayOnlineResult>('POST', `/portal/me/finance/invoices/${FINANCE_IDS.invoice1}/pay`, guardian, { idempotencyKey: 'k-1' });
    expect(start.body.provider).toBe('fake');
    const authority = authorityFromRedirect(start.body.redirectUrl)!;
    expect(authority).toMatch(/^FAKE/);
    expect((await call('GET', `/finance/gateway/fake/callback/${start.body.paymentId}?Authority=WRONG&Status=OK`, null)).status).toBe(404);
    const done = await call<PaymentStatusResult>('GET', `/finance/gateway/fake/callback/${start.body.paymentId}?Authority=${authority}&Status=OK`, null);
    expect(done.body.status).toBe('succeeded');
    expect(done.body.receiptNumber).toBe('R-000002');
    const replay = await call<PaymentStatusResult>('POST', `/portal/me/finance/payments/${start.body.paymentId}/verify`, guardian, {});
    expect(replay.body.receiptNumber).toBe('R-000002');
    const account = await call<StudentAccount>('GET', `/portal/me/finance/students/${MOCK_IDS.student1}/account`, guardian);
    expect(account.body.balance).toBe(0);
  });

  it('withholds portal grades when results blocking is on and an installment is overdue', async () => {
    const officer = await login('finance.officer');
    const settings = await call('GET', '/finance/settings', officer);
    await call('PUT', '/finance/settings', officer, { ...settings.body, blockResultsOnOverdue: true });
    const student = await login('student.reza');
    const grades = await call('GET', `/portal/me/students/${MOCK_IDS.student1}/grades`, student);
    expect(grades.status).toBe(403);
    expect(grades.body.code).toBe('finance.results_blocked');
  });

  it('a client built on the endpoints talks to the mock', async () => {
    const token = await login('finance.officer');
    const api = createEndpoints(createApiClient({ baseUrl: MOCK_API_BASE_URL, fetch: backend.fetch, headers: () => ({ Authorization: `Bearer ${token}` }) }));
    const page = await api.finance.invoices.list({ overdueOnly: true });
    expect(page.items.map((i) => i.number)).toEqual(['INV-000001']);
    const file = await api.finance.reports.csv('outstanding');
    expect(file.fileName).toBe('outstanding.csv');
  });
});

describe('mock workflow', () => {
  it('inbox applies maker-checker; approve completes the instance with history', async () => {
    const requester = await login('finance.officer');
    expect((await call('GET', '/workflow/inbox', requester)).body.totalCount).toBe(0);
    const approver = await login('demo.admin');
    const inbox = await call('GET', '/workflow/inbox', approver);
    expect(inbox.body.items.map((i: { id: string }) => i.id)).toEqual([WORKFLOW_IDS.instanceSample]);
    const detail = await call<WorkflowInstance>('GET', `/workflow/instances/${WORKFLOW_IDS.instanceSample}`, approver);
    // demo.admin also manages instances in North, so may cancel.
    expect(detail.body.allowedActions).toEqual(['approve', 'reject', 'return', 'cancel']);
    const rejectNoComment = await call('POST', `/workflow/instances/${WORKFLOW_IDS.instanceSample}/reject`, approver, { rowVersion: detail.body.rowVersion });
    expect(rejectNoComment.body.errors.comment).toEqual(['validation.required']);
    const approved = await call<WorkflowInstance>('POST', `/workflow/instances/${WORKFLOW_IDS.instanceSample}/approve`, approver, { rowVersion: detail.body.rowVersion, requestId: 'r1' });
    expect(approved.body.status).toBe('approved');
    expect(approved.body.history.map((h) => h.action)).toEqual(['started', 'approved']);
    const retry = await call<WorkflowInstance>('POST', `/workflow/instances/${WORKFLOW_IDS.instanceSample}/approve`, approver, { rowVersion: detail.body.rowVersion, requestId: 'r1' });
    expect(retry.status).toBe(200);
    const own = await call('POST', `/workflow/instances/${WORKFLOW_IDS.instanceMine}/approve`, approver, { rowVersion: 'x' });
    expect(own.status).toBe(409); // stale rowVersion is checked first; with the right one it is self-approval
  });

  it('definitions: draft versions are editable, publishing retires the previous version', async () => {
    const admin = await login('demo.admin');
    const defs = await call('GET', '/workflow/definitions', admin);
    const id = defs.body[0].id;
    const draft = await call('POST', `/workflow/definitions/${id}/versions`, admin);
    expect(draft.body.versions.at(-1).status).toBe('draft');
    expect((await call('POST', `/workflow/definitions/${id}/versions`, admin)).body.code).toBe('workflow.draft_exists');
    const v2 = draft.body.versions.at(-1);
    const bad = await call('PUT', `/workflow/definitions/${id}/versions/2`, admin, { rowVersion: v2.rowVersion, requireDistinctApprovers: true, steps: [{ name: 'X', permission: 'nope.read', allowReturn: true }] });
    expect(bad.body.errors['steps[0].permission']).toEqual(['validation.unknown_permission']);
    const saved = await call('PUT', `/workflow/definitions/${id}/versions/2`, admin, { rowVersion: v2.rowVersion, requireDistinctApprovers: true, steps: [{ name: 'Finance', permission: 'finance.approve', slaHours: 24, allowReturn: false }] });
    const published = await call('POST', `/workflow/definitions/${id}/versions/2/publish`, admin, { rowVersion: saved.body.rowVersion });
    expect(published.body.publishedVersion).toBe(2);
    expect(published.body.versions.map((v: { status: string }) => v.status)).toEqual(['retired', 'published']);
  });
});

describe('money helpers', () => {
  it('parses Persian digits and separators, enforcing the currency precision', () => {
    expect(parseAmount('۱۲٬۰۰۰٬۰۰۰', 'IRR')).toEqual({ ok: true, value: 12_000_000 });
    expect(parseAmount('1,500', 'IRR')).toEqual({ ok: true, value: 1500 });
    expect(parseAmount('10.5', 'IRR')).toEqual({ ok: false, reason: 'precision' });
    expect(parseAmount('10.50', 'EUR')).toEqual({ ok: true, value: 10.5 });
    expect(parseAmount('0', 'IRR')).toEqual({ ok: false, reason: 'notPositive' });
    expect(parseAmount('abc', 'IRR')).toEqual({ ok: false, reason: 'invalid' });
    expect(parseAmount(' ', 'IRR')).toEqual({ ok: false, reason: 'empty' });
  });

  it('reads the authority from the fake gateway URL', () => {
    expect(authorityFromRedirect('https://fake-gateway.invalid/pay/FAKEABC123')).toBe('FAKEABC123');
    expect(authorityFromRedirect('not a url')).toBeNull();
  });
});
