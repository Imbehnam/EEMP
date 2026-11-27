/**
 * Mock of the Milestone 3 finance API (development and tests only; NOT a security boundary).
 * Mirrors work/eemp-milestone3-contract.md closely enough for the UI: fee setup, invoices (draft → issued →
 * partiallyPaid → paid, void, credit notes), idempotent generation, discounts with maker-checker, installments, late
 * fees, manual payments with gap-free receipts, allocation and credit, refunds with approval, the fake online gateway,
 * portal views, reports with CSV and stand-in PDFs. The server-side rules are simplified (single currency IRR).
 */
import type {
  ApprovalItem,
  FeeItem,
  FeeType,
  FinanceSettings,
  Installment,
  Invoice,
  InvoiceAdjustment,
  InvoiceDiscount,
  InvoiceLine,
  InvoiceSummary,
  Payment,
  Refund,
} from '../finance-types';
import type { Guid } from '../types';
import { binary, mockPdf } from './files';
import { assertValid, HttpError, json, paginate, uuid } from './http';
import type { MockTenantData, MockUser } from './seed';

export interface MockFinance {
  settings: Omit<FinanceSettings, 'availableGateways'>;
  feeTypes: FeeType[];
  feeItems: FeeItem[];
  invoices: Invoice[];
  payments: (Payment & { idempotencyKey: string | null; authority: string | null; targetInvoiceId: Guid | null; targetInstallmentId: Guid | null; createdBy: Guid | null })[];
  refunds: Refund[];
  /** enrollmentId|feeItemId → invoice id (a charge is generated at most once). */
  charges: Record<string, Guid>;
  series: Record<'INV' | 'R' | 'CN', number>;
}

export const FINANCE_IDS = {
  feeTypeTuition: 'f0000000-0000-4000-8000-000000000001',
  feeTypeBooks: 'f0000000-0000-4000-8000-000000000002',
  feeItemTuition: 'f0000000-0000-4000-8000-000000000011',
  feeItemBooks: 'f0000000-0000-4000-8000-000000000012',
  invoice1: 'f0000000-0000-4000-8000-000000000021',
  payment1: 'f0000000-0000-4000-8000-000000000031',
} as const;

export const emptyFinance = (rv: () => string): MockFinance => ({
  settings: {
    currency: 'IRR', vatEnabled: false, vatRatePercent: 0, lateFeeEnabled: false, lateFeeType: 'fixed', lateFeeValue: 0, lateFeeGraceDays: 0,
    siblingDiscountPercent: 0, blockResultsOnOverdue: false, onlinePaymentsEnabled: false, rowVersion: rv(),
  },
  feeTypes: [], feeItems: [], invoices: [], payments: [], refunds: [], charges: {}, series: { INV: 0, R: 0, CN: 0 },
});

const fmtNo = (series: string, n: number) => `${series}-${String(n).padStart(6, '0')}`;

/** Demo tenant: tuition 12,000,000 IRR for term 1; INV-000001 for ST-1001 in two installments; a 5,000,000 IRR cash receipt. */
export function createFinanceSeed(
  rv: () => string,
  ids: { student1: string; branchNorth: string; userFinance: string },
  academic: { term1: string; enrollmentOf: (studentId: string) => string | undefined },
): MockFinance {
  const f = emptyFinance(rv);
  f.settings.onlinePaymentsEnabled = true;
  f.feeTypes.push(
    { id: FINANCE_IDS.feeTypeTuition, code: 'TUITION', name: 'Tuition', taxable: false, isActive: true, rowVersion: rv() },
    { id: FINANCE_IDS.feeTypeBooks, code: 'BOOKS', name: 'Books', taxable: true, isActive: true, rowVersion: rv() },
  );
  f.feeItems.push(
    { id: FINANCE_IDS.feeItemTuition, feeTypeId: FINANCE_IDS.feeTypeTuition, feeTypeCode: 'TUITION', name: 'Tuition - Term 1', amount: 12_000_000, currency: 'IRR', termId: academic.term1, courseId: null, branchId: null, isActive: true, rowVersion: rv() },
    { id: FINANCE_IDS.feeItemBooks, feeTypeId: FINANCE_IDS.feeTypeBooks, feeTypeCode: 'BOOKS', name: 'Book pack', amount: 1_500_000, currency: 'IRR', termId: null, courseId: null, branchId: null, isActive: true, rowVersion: rv() },
  );
  const enrollmentId = academic.enrollmentOf(ids.student1) ?? null;
  const inv: Invoice = {
    id: FINANCE_IDS.invoice1, number: fmtNo('INV', 1), studentId: ids.student1, studentName: 'رضا احمدی', studentNumber: 'ST-1001', branchId: ids.branchNorth,
    termId: academic.term1, status: 'issued', currency: 'IRR', issueDate: '2026-09-23', dueDate: '2026-12-21', subtotal: 12_000_000, discountTotal: 0,
    taxRatePercent: 0, taxTotal: 0, lateFeeTotal: 0, creditTotal: 0, total: 12_000_000, paidAmount: 0, balance: 12_000_000, isOverdue: false, note: null,
    voidReason: null, issuedAt: '2026-09-23T06:00:00Z', voidedAt: null,
    lines: [{ id: uuid(), feeItemId: FINANCE_IDS.feeItemTuition, enrollmentId, description: 'Tuition - Term 1', quantity: 1, unitPrice: 12_000_000, amount: 12_000_000, taxable: false }],
    discounts: [],
    installments: [
      { id: uuid(), seqNo: 1, dueDate: '2026-10-22', amount: 6_000_000, paidAmount: 0, lateFeeCharged: false, status: 'open' },
      { id: uuid(), seqNo: 2, dueDate: '2026-12-21', amount: 6_000_000, paidAmount: 0, lateFeeCharged: false, status: 'open' },
    ],
    adjustments: [], rowVersion: rv(),
  };
  f.invoices.push(inv);
  if (enrollmentId) f.charges[`${enrollmentId}|${FINANCE_IDS.feeItemTuition}`] = inv.id;
  f.series.INV = 1;
  f.payments.push({
    id: FINANCE_IDS.payment1, studentId: ids.student1, studentName: 'رضا احمدی', branchId: ids.branchNorth, method: 'cash', status: 'succeeded', amount: 5_000_000,
    currency: 'IRR', receivedOn: '2026-10-01', reference: null, cheque: null, provider: null, providerRef: null, receiptNumber: fmtNo('R', 1), allocated: 0,
    unallocated: 5_000_000, refunded: 0, failureCode: null, allocations: [], createdAt: '2026-10-01T06:00:00Z', rowVersion: rv(), idempotencyKey: null,
    authority: null, targetInvoiceId: inv.id, targetInstallmentId: null, createdBy: ids.userFinance,
  });
  f.series.R = 1;
  allocate(f, f.payments[0]!, 5_000_000, inv.id, { nextVersion: rv, today: () => '2026-10-01' });
  return f;
}

export interface FinanceContext {
  user: MockUser;
  data: MockTenantData;
  scope: (permission: string) => Set<string> | null;
  now: () => number;
  nextVersion: () => string;
  today: () => string;
}

const includes = (scope: Set<string> | null, branchId: string) => scope === null || scope.has(branchId);
const notFound = () => new HttpError(404, 'resource.not_found');
const str = (v: unknown) => (typeof v === 'string' ? v.trim() : '');
const optStr = (v: unknown) => (typeof v === 'string' && v.trim() !== '' ? v.trim() : null);
const num = (v: unknown) => (typeof v === 'number' && Number.isFinite(v) ? v : null);
const ISO = /^\d{4}-\d{2}-\d{2}$/;
const round0 = (v: number) => Math.sign(v) * Math.round(Math.abs(v));
const addDays = (iso: string, days: number) => new Date(Date.parse(`${iso}T00:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10);
const daysBetween = (a: string, b: string) => Math.round((Date.parse(`${b}T00:00:00Z`) - Date.parse(`${a}T00:00:00Z`)) / 86_400_000);

function checkVersion(current: string, body: Record<string, unknown>) {
  if (typeof body.rowVersion !== 'string' || body.rowVersion === '') {
    throw new HttpError(400, 'validation.failed', { errors: { rowVersion: ['validation.required'] } });
  }
  if (body.rowVersion !== current) throw new HttpError(409, 'concurrency.conflict');
}

function amountField(errors: Record<string, string[]>, field: string, value: unknown, required = true): number | null {
  const n = num(value);
  if (n === null) {
    if (required || (value !== undefined && value !== null)) errors[field] = [value === undefined || value === null ? 'validation.required' : 'validation.invalid_value'];
    return null;
  }
  if (n <= 0 || !Number.isInteger(n)) {
    errors[field] = ['validation.invalid_value'];
    return null;
  }
  return n;
}

/** Recomputes derived invoice fields (amounts are whole rials). */
export function recalc(inv: Invoice, today: string) {
  inv.subtotal = inv.lines.reduce((s, l) => s + l.amount, 0);
  let remaining = inv.subtotal;
  for (const d of inv.discounts.filter((x) => x.status === 'approved')) {
    const raw = d.type === 'percent' ? round0((inv.subtotal * d.value) / 100) : d.value;
    d.amount = Math.min(raw, remaining);
    remaining -= d.amount;
  }
  inv.discountTotal = inv.subtotal - remaining;
  const taxable = inv.lines.filter((l) => l.taxable).reduce((s, l) => s + l.amount, 0);
  const share = inv.subtotal > 0 ? taxable - round0((inv.discountTotal * taxable) / inv.subtotal) : 0;
  inv.taxTotal = inv.taxRatePercent > 0 ? round0((share * inv.taxRatePercent) / 100) : 0;
  inv.total = inv.subtotal - inv.discountTotal + inv.taxTotal;
  inv.lateFeeTotal = inv.adjustments.filter((a) => a.kind === 'lateFee').reduce((s, a) => s + a.amount, 0);
  inv.creditTotal = inv.adjustments.filter((a) => a.kind === 'creditNote').reduce((s, a) => s + a.amount, 0);
  inv.balance = inv.status === 'void' ? 0 : inv.total + inv.lateFeeTotal - inv.creditTotal - inv.paidAmount;
  if (inv.status === 'issued' || inv.status === 'partiallyPaid' || inv.status === 'paid') {
    inv.status = inv.balance <= 0 ? 'paid' : inv.paidAmount > 0 ? 'partiallyPaid' : 'issued';
  }
  for (const i of inv.installments) i.status = i.paidAmount >= i.amount ? 'paid' : i.dueDate < today ? 'overdue' : 'open';
  const open = inv.status === 'issued' || inv.status === 'partiallyPaid';
  inv.isOverdue = open && inv.balance > 0 && (inv.installments.length ? inv.installments.some((i) => i.status === 'overdue') : !!inv.dueDate && inv.dueDate < today);
}

const summary = (i: Invoice): InvoiceSummary => ({
  id: i.id, number: i.number, studentId: i.studentId, studentName: i.studentName, studentNumber: i.studentNumber, branchId: i.branchId, termId: i.termId,
  status: i.status, currency: i.currency, issueDate: i.issueDate, dueDate: i.dueDate, total: i.total, paidAmount: i.paidAmount, balance: i.balance,
  isOverdue: i.isOverdue, rowVersion: i.rowVersion,
});

function publicPayment(p: MockFinance['payments'][number]): Payment {
  const { idempotencyKey: _k, authority: _a, targetInvoiceId: _t, targetInstallmentId: _i, createdBy: _c, ...rest } = p;
  return rest;
}

/** True when the tenant blocks results and the student has an overdue issued balance (DL-065). */
export function resultsBlocked(data: MockTenantData, studentId: string, today: string): boolean {
  const f = data.finance;
  if (!f.settings.blockResultsOnOverdue) return false;
  return f.invoices.some((i) => {
    if (i.studentId !== studentId) return false;
    recalc(i, today);
    return i.isOverdue;
  });
}

/** Anonymous gateway callback (no session): finds the payment in any tenant, checks the authority, settles it. */
export function gatewayCallback(tenants: MockTenantData[], provider: string, paymentId: string, url: URL, ctx: { nextVersion: () => string; today: () => string }) {
  for (const data of tenants) {
    const p = data.finance.payments.find((x) => x.id === paymentId);
    if (!p) continue;
    const authority = url.searchParams.get('Authority') ?? url.searchParams.get('authority');
    if (p.provider !== provider || !p.authority || p.authority !== authority) throw notFound();
    return json(200, settle(data, p, url.searchParams.get('Status') ?? url.searchParams.get('status'), ctx));
  }
  throw notFound();
}

function settle(data: MockTenantData, p: MockFinance['payments'][number], status: string | null, ctx: { nextVersion: () => string; today: () => string }) {
  if (p.status === 'pending') {
    if ((status ?? '').toUpperCase() !== 'OK') {
      p.status = 'cancelled';
      p.failureCode = 'cancelled_by_payer';
    } else {
      succeed(data.finance, p, ctx);
    }
    p.rowVersion = ctx.nextVersion();
  }
  return { paymentId: p.id, status: p.status, receiptNumber: p.receiptNumber, failureCode: p.failureCode };
}

/** Marks a payment succeeded: receipt number, allocation (requested invoice, else oldest due), remainder → credit. */
function succeed(f: MockFinance, p: MockFinance['payments'][number], ctx: { nextVersion: () => string; today: () => string }) {
  p.status = 'succeeded';
  p.receiptNumber = fmtNo('R', ++f.series.R);
  allocate(f, p, p.amount - p.allocated - p.refunded, p.targetInvoiceId, ctx);
}

function allocate(f: MockFinance, p: MockFinance['payments'][number], amount: number, invoiceId: Guid | null, ctx: { nextVersion: () => string; today: () => string }) {
  let left = amount;
  const open = (i: Invoice) => (i.status === 'issued' || i.status === 'partiallyPaid') && i.studentId === p.studentId;
  const targets = invoiceId
    ? f.invoices.filter((i) => i.id === invoiceId && open(i))
    : f.invoices.filter(open).sort((a, b) => (a.dueDate ?? '9999').localeCompare(b.dueDate ?? '9999') || (a.number ?? '').localeCompare(b.number ?? ''));
  for (const inv of targets) {
    if (left <= 0) break;
    recalc(inv, ctx.today());
    let take = Math.min(left, inv.balance);
    if (take <= 0) continue;
    left -= take;
    inv.paidAmount += take;
    const insts = [...inv.installments].sort((a, b) => a.dueDate.localeCompare(b.dueDate));
    if (insts.length === 0) p.allocations.push({ invoiceId: inv.id, invoiceNumber: inv.number, installmentId: null, amount: take });
    for (const i of insts) {
      if (take <= 0) break;
      const part = Math.min(take, i.amount - i.paidAmount);
      if (part <= 0) continue;
      i.paidAmount += part;
      take -= part;
      p.allocations.push({ invoiceId: inv.id, invoiceNumber: inv.number, installmentId: i.id, amount: part });
    }
    if (take > 0) p.allocations.push({ invoiceId: inv.id, invoiceNumber: inv.number, installmentId: null, amount: take });
    recalc(inv, ctx.today());
    inv.rowVersion = ctx.nextVersion();
  }
  p.allocated = p.allocations.reduce((s, a) => s + a.amount, 0);
  p.unallocated = p.amount - p.allocated - p.refunded;
}

/** Handles a finance route, or returns null when the path is not part of the M3 API. */
export function financeRoutes(ctx: FinanceContext, seg: string[], method: string, url: URL, body: Record<string, unknown>): Response | null {
  const { user, data } = ctx;
  const f = data.finance;
  const nv = ctx.nextVersion;
  const nowIso = () => new Date(ctx.now()).toISOString();
  const today = ctx.today;

  const demand = (permission: string) => {
    const s = ctx.scope(permission);
    if (s !== null && s.size === 0) throw new HttpError(403, 'authorization.forbidden');
    return s;
  };
  /** Branch-scoped record: out of read scope → 404, readable but not changeable → 403 forbidden_scope. */
  const inScope = (permission: string, branchId: string) => {
    const s = demand(permission);
    if (includes(s, branchId)) return;
    if (includes(ctx.scope('finance.read'), branchId)) throw new HttpError(403, 'authorization.forbidden_scope');
    throw notFound();
  };
  const student = (id: string) => data.students.find((s) => s.id === id);
  const findInvoice = (id: string | undefined, permission = 'finance.read') => {
    const inv = f.invoices.find((i) => i.id === id);
    if (!inv) throw notFound();
    if (!includes(ctx.scope('finance.read'), inv.branchId) && !includes(ctx.scope(permission), inv.branchId)) throw notFound();
    inScope(permission, inv.branchId);
    recalc(inv, today());
    return inv;
  };
  const findPayment = (id: string | undefined, permission = 'finance.read') => {
    const p = f.payments.find((x) => x.id === id);
    if (!p) throw notFound();
    if (!includes(ctx.scope('finance.read'), p.branchId) && !includes(ctx.scope(permission), p.branchId)) throw notFound();
    inScope(permission, p.branchId);
    return p;
  };
  const settingsDto = () => ({ ...f.settings, availableGateways: ['fake'] });
  const approvalItems = (status: string): ApprovalItem[] => [
    ...f.invoices.flatMap((inv) => inv.discounts.filter((d) => d.status === status).map<ApprovalItem>((d) => ({
      type: 'discount', id: d.id, invoiceId: inv.id, paymentId: null, studentId: inv.studentId, studentName: inv.studentName, branchId: inv.branchId,
      amount: d.type === 'fixed' ? d.value : round0((inv.subtotal * d.value) / 100), currency: inv.currency,
      detail: `${d.kind} ${d.type === 'percent' ? `${d.value}%` : d.value}`, reason: d.reason, requestedBy: d.requestedBy, requestedAt: d.requestedAt, rowVersion: d.rowVersion,
    }))),
    ...f.refunds.filter((r) => r.status === status).map<ApprovalItem>((r) => ({
      type: 'refund', id: r.id, invoiceId: null, paymentId: r.paymentId, studentId: r.studentId, studentName: student(r.studentId) ? `${student(r.studentId)!.firstName} ${student(r.studentId)!.lastName}` : '',
      branchId: r.branchId, amount: r.amount, currency: r.currency, detail: f.payments.find((p) => p.id === r.paymentId)?.receiptNumber ?? '', reason: r.reason,
      requestedBy: r.requestedBy, requestedAt: r.requestedAt, rowVersion: r.rowVersion,
    })),
  ].sort((a, b) => a.requestedAt.localeCompare(b.requestedAt));

  const pdfLang = () => {
    const lang = url.searchParams.get('lang');
    if (lang && !['fa-IR', 'fa', 'en', 'tr', 'ar'].includes(lang)) {
      throw new HttpError(400, 'validation.failed', { errors: { lang: ['validation.unsupported_locale'] } });
    }
    return lang ?? 'fa-IR';
  };
  const invoicePdf = (inv: Invoice) => {
    if (inv.status === 'draft') throw new HttpError(422, 'finance.invoice_not_issued');
    return binary(mockPdf([`Invoice ${inv.number}`, `Student ${inv.studentNumber}`, `Total ${inv.total} ${inv.currency}`, `Balance ${inv.balance}`, `lang ${pdfLang()}`]), 'application/pdf', `invoice-${inv.number}.pdf`);
  };
  const receiptPdf = (p: MockFinance['payments'][number]) => {
    if (p.status !== 'succeeded' || !p.receiptNumber) throw new HttpError(422, 'finance.receipt_unavailable');
    return binary(mockPdf([`Receipt ${p.receiptNumber}`, `Amount ${p.amount} ${p.currency}`, `Method ${p.method}`, `lang ${pdfLang()}`]), 'application/pdf', `receipt-${p.receiptNumber}.pdf`);
  };
  const studentAccount = (studentId: string, portal: boolean) => {
    const s = student(studentId);
    if (!s) throw notFound();
    const invoices = f.invoices.filter((i) => i.studentId === studentId && (!portal || i.status !== 'draft'));
    invoices.forEach((i) => recalc(i, today()));
    const payments = f.payments.filter((p) => p.studentId === studentId && (!portal || p.status !== 'pending' || p.method === 'online'));
    const live = invoices.filter((i) => i.status !== 'draft' && i.status !== 'void');
    return {
      studentId, studentName: `${s.firstName} ${s.lastName}`, studentNumber: s.studentNumber ?? '', currency: f.settings.currency,
      totalInvoiced: live.reduce((x, i) => x + i.total + i.lateFeeTotal - i.creditTotal, 0),
      totalPaid: payments.filter((p) => p.status === 'succeeded').reduce((x, p) => x + p.amount - p.refunded, 0),
      balance: live.reduce((x, i) => x + i.balance, 0),
      creditBalance: payments.filter((p) => p.status === 'succeeded').reduce((x, p) => x + p.unallocated, 0),
      overdue: live.filter((i) => i.isOverdue).reduce((x, i) => x + i.balance, 0),
      invoices: invoices.map(summary).sort((a, b) => (b.issueDate ?? '').localeCompare(a.issueDate ?? '')),
      payments: payments.map(publicPayment).sort((a, b) => b.receivedOn.localeCompare(a.receivedOn)),
    };
  };
  const linesFrom = (raw: unknown, errors: Record<string, string[]>): InvoiceLine[] => {
    const list = Array.isArray(raw) ? (raw as Record<string, unknown>[]) : [];
    if (list.length === 0) errors.lines = ['validation.required'];
    return list.map((l, i): InvoiceLine | null => {
      const qty = num(l.quantity) ?? 1;
      if (qty < 1 || !Number.isInteger(qty)) errors[`lines[${i}].quantity`] = ['validation.invalid_value'];
      if (l.feeItemId) {
        const item = f.feeItems.find((x) => x.id === l.feeItemId);
        if (!item) {
          errors[`lines[${i}].feeItemId`] = ['validation.invalid_value'];
          return null;
        }
        if (!item.isActive) throw new HttpError(422, 'finance.fee_item_inactive');
        const type = f.feeTypes.find((x) => x.id === item.feeTypeId);
        return { id: uuid(), feeItemId: item.id, enrollmentId: null, description: item.name, quantity: qty, unitPrice: item.amount, amount: item.amount * qty, taxable: !!type?.taxable };
      }
      const description = str(l.description);
      if (!description) errors[`lines[${i}].description`] = ['validation.required'];
      const unit = amountField(errors, `lines[${i}].unitPrice`, l.unitPrice);
      return { id: uuid(), feeItemId: null, enrollmentId: null, description, quantity: qty, unitPrice: unit ?? 0, amount: (unit ?? 0) * qty, taxable: false };
    }).filter((x): x is InvoiceLine => x !== null);
  };
  const newInvoice = (studentId: string, termId: string | null, dueDate: string | null, note: string | null, lines: InvoiceLine[]): Invoice => {
    const s = student(studentId)!;
    const inv: Invoice = {
      id: uuid(), number: null, studentId, studentName: `${s.firstName} ${s.lastName}`, studentNumber: s.studentNumber ?? '', branchId: s.branchId, termId,
      status: 'draft', currency: f.settings.currency, issueDate: null, dueDate, subtotal: 0, discountTotal: 0, taxRatePercent: 0, taxTotal: 0, lateFeeTotal: 0,
      creditTotal: 0, total: 0, paidAmount: 0, balance: 0, isOverdue: false, note, voidReason: null, issuedAt: null, voidedAt: null, lines, discounts: [],
      installments: [], adjustments: [], rowVersion: nv(),
    };
    recalc(inv, today());
    return inv;
  };
  const decideMaker = (requestedBy: string | null) => {
    if (requestedBy === user.id) throw new HttpError(403, 'finance.maker_checker');
  };

  /* ----- portal ----- */
  if (seg[0] === 'portal' && seg[1] === 'me' && seg[2] === 'finance') {
    const own = data.students.find((s) => s.userId === user.id);
    const guardian = data.guardians.find((g) => g.userId === user.id);
    const mine = (studentId: string) => own?.id === studentId || (!!guardian && !!student(studentId)?.guardians.some((g) => g.guardianId === guardian.id));
    const portalInvoice = (id?: string) => {
      const inv = f.invoices.find((i) => i.id === id);
      if (!inv || inv.status === 'draft' || !mine(inv.studentId)) throw notFound();
      recalc(inv, today());
      return inv;
    };
    const portalPayment = (id?: string) => {
      const p = f.payments.find((x) => x.id === id);
      if (!p || !mine(p.studentId)) throw notFound();
      return p;
    };
    if (seg[3] === 'students' && seg[5] === 'account' && method === 'GET') {
      if (!mine(seg[4]!)) throw notFound();
      return json(200, studentAccount(seg[4]!, true));
    }
    if (seg[3] === 'invoices' && seg[5] === 'pdf' && method === 'GET') return invoicePdf(portalInvoice(seg[4]));
    if (seg[3] === 'invoices' && seg[5] === 'pay' && method === 'POST') {
      const inv = portalInvoice(seg[4]);
      const key = str(body.idempotencyKey);
      if (!key) throw new HttpError(400, 'validation.failed', { errors: { idempotencyKey: ['validation.required'] } });
      if (!f.settings.onlinePaymentsEnabled) throw new HttpError(422, 'finance.online_payments_disabled');
      const existing = f.payments.find((p) => p.idempotencyKey === key);
      if (existing) return json(200, { paymentId: existing.id, redirectUrl: `https://fake-gateway.invalid/pay/${existing.authority}`, provider: 'fake' });
      if (inv.status !== 'issued' && inv.status !== 'partiallyPaid') throw new HttpError(409, 'finance.invalid_state', { params: { status: inv.status } });
      let outstanding = inv.balance;
      if (body.installmentId) {
        const inst = inv.installments.find((i) => i.id === body.installmentId);
        if (!inst) throw new HttpError(400, 'validation.failed', { errors: { installmentId: ['validation.invalid_value'] } });
        outstanding = Math.min(inst.amount - inst.paidAmount, inv.balance);
      }
      const errors: Record<string, string[]> = {};
      const partial = amountField(errors, 'amount', body.amount, false);
      assertValid(errors);
      if (partial !== null && partial > outstanding) throw new HttpError(422, 'finance.amount_exceeds_balance', { params: { balance: outstanding } });
      const authority = `FAKE${uuid().replace(/-/g, '').slice(0, 24).toUpperCase()}`;
      const p: MockFinance['payments'][number] = {
        id: uuid(), studentId: inv.studentId, studentName: inv.studentName, branchId: inv.branchId, method: 'online', status: 'pending', amount: partial ?? outstanding,
        currency: inv.currency, receivedOn: today(), reference: null, cheque: null, provider: 'fake', providerRef: null, receiptNumber: null, allocated: 0,
        unallocated: 0, refunded: 0, failureCode: null, allocations: [], createdAt: nowIso(), rowVersion: nv(), idempotencyKey: key, authority,
        targetInvoiceId: inv.id, targetInstallmentId: (body.installmentId as string) ?? null, createdBy: user.id,
      };
      f.payments.push(p);
      return json(200, { paymentId: p.id, redirectUrl: `https://fake-gateway.invalid/pay/${authority}`, provider: 'fake' });
    }
    if (seg[3] === 'invoices' && seg[4] && seg.length === 5 && method === 'GET') return json(200, portalInvoice(seg[4]));
    if (seg[3] === 'payments' && seg[5] === 'verify' && method === 'POST') {
      const p = portalPayment(seg[4]);
      if (p.method !== 'online') throw notFound();
      return json(200, settle(data, p, 'OK', { nextVersion: nv, today }));
    }
    if (seg[3] === 'payments' && seg[5] === 'receipt' && seg[6] === 'pdf' && method === 'GET') return receiptPdf(portalPayment(seg[4]));
    if (seg[3] === 'payments' && seg[4] && seg.length === 5 && method === 'GET') return json(200, publicPayment(portalPayment(seg[4])));
    throw notFound();
  }

  if (seg[0] !== 'finance') return null;

  /* ----- settings and fee setup ----- */
  if (seg[1] === 'settings' && seg.length === 2) {
    if (method === 'GET') {
      if (ctx.scope('finance.setup.manage')?.size === 0) demand('finance.read');
      return json(200, settingsDto());
    }
    if (method === 'PUT') {
      demand('finance.setup.manage');
      checkVersion(f.settings.rowVersion, body);
      const errors: Record<string, string[]> = {};
      const pct = (field: string, v: unknown) => {
        const n = num(v) ?? 0;
        if (n < 0 || n > 100 || Math.round(n * 100) !== n * 100) errors[field] = ['validation.invalid_value'];
        return n;
      };
      const next = {
        vatEnabled: !!body.vatEnabled, vatRatePercent: pct('vatRatePercent', body.vatRatePercent), lateFeeEnabled: !!body.lateFeeEnabled,
        lateFeeType: body.lateFeeType === 'percent' ? ('percent' as const) : ('fixed' as const), lateFeeValue: num(body.lateFeeValue) ?? 0,
        lateFeeGraceDays: num(body.lateFeeGraceDays) ?? 0, siblingDiscountPercent: pct('siblingDiscountPercent', body.siblingDiscountPercent),
        blockResultsOnOverdue: !!body.blockResultsOnOverdue, onlinePaymentsEnabled: !!body.onlinePaymentsEnabled,
      };
      if (next.lateFeeValue < 0) errors.lateFeeValue = ['validation.invalid_value'];
      if (next.lateFeeGraceDays < 0 || !Number.isInteger(next.lateFeeGraceDays)) errors.lateFeeGraceDays = ['validation.invalid_value'];
      assertValid(errors);
      Object.assign(f.settings, next, { rowVersion: nv() });
      return json(200, settingsDto());
    }
  }
  if (seg[1] === 'fee-types') {
    if (method === 'GET' && seg.length === 2) {
      if (ctx.scope('finance.setup.manage')?.size === 0) demand('finance.read');
      return json(200, [...f.feeTypes].sort((a, b) => a.code.localeCompare(b.code)));
    }
    demand('finance.setup.manage');
    const existing = seg[2] ? f.feeTypes.find((x) => x.id === seg[2]) : undefined;
    if (seg[2] && !existing) throw notFound();
    if (method === 'DELETE' && existing) {
      if (f.feeItems.some((i) => i.feeTypeId === existing.id)) throw new HttpError(409, 'finance.fee_type_in_use');
      f.feeTypes = f.feeTypes.filter((x) => x !== existing);
      return json(204, null);
    }
    if (method === 'POST' || method === 'PUT') {
      if (existing) checkVersion(existing.rowVersion, body);
      const errors: Record<string, string[]> = {};
      const code = str(body.code).toUpperCase();
      if (!code) errors.code = ['validation.required'];
      if (!str(body.name)) errors.name = ['validation.required'];
      assertValid(errors);
      if (f.feeTypes.some((x) => x.code === code && x.id !== existing?.id)) throw new HttpError(409, 'resource.duplicate', { params: { field: 'code' } });
      const value: FeeType = { id: existing?.id ?? uuid(), code, name: str(body.name), taxable: !!body.taxable, isActive: body.isActive !== false, rowVersion: nv() };
      if (existing) Object.assign(existing, value);
      else f.feeTypes.push(value);
      return json(existing ? 200 : 201, value);
    }
  }
  if (seg[1] === 'fee-items') {
    if (method === 'GET' && seg.length === 2) {
      if (ctx.scope('finance.setup.manage')?.size === 0) demand('finance.read');
      const p = url.searchParams;
      return json(200, f.feeItems
        .filter((i) => (!p.get('termId') || i.termId === p.get('termId')) && (!p.get('feeTypeId') || i.feeTypeId === p.get('feeTypeId')) && (p.get('activeOnly') !== 'true' || i.isActive))
        .sort((a, b) => a.name.localeCompare(b.name)));
    }
    demand('finance.setup.manage');
    const existing = seg[2] ? f.feeItems.find((x) => x.id === seg[2]) : undefined;
    if (seg[2] && !existing) throw notFound();
    if (method === 'DELETE' && existing) {
      if (f.invoices.some((i) => i.lines.some((l) => l.feeItemId === existing.id))) throw new HttpError(409, 'finance.fee_item_in_use');
      f.feeItems = f.feeItems.filter((x) => x !== existing);
      return json(204, null);
    }
    if (method === 'POST' || method === 'PUT') {
      if (existing) checkVersion(existing.rowVersion, body);
      const errors: Record<string, string[]> = {};
      const type = f.feeTypes.find((t) => t.id === body.feeTypeId);
      if (!type) errors.feeTypeId = [body.feeTypeId ? 'validation.invalid_value' : 'validation.required'];
      if (!str(body.name)) errors.name = ['validation.required'];
      const amount = amountField(errors, 'amount', body.amount);
      assertValid(errors);
      const value: FeeItem = {
        id: existing?.id ?? uuid(), feeTypeId: type!.id, feeTypeCode: type!.code, name: str(body.name), amount: amount!, currency: f.settings.currency,
        termId: optStr(body.termId), courseId: optStr(body.courseId), branchId: optStr(body.branchId), isActive: body.isActive !== false, rowVersion: nv(),
      };
      if (existing) Object.assign(existing, value);
      else f.feeItems.push(value);
      return json(existing ? 200 : 201, value);
    }
  }

  /* ----- invoices ----- */
  if (seg[1] === 'invoices') {
    if (seg.length === 2 && method === 'GET') {
      const scope = demand('finance.read');
      const p = url.searchParams;
      const rows = f.invoices
        .filter((i) => includes(scope, i.branchId))
        .map((i) => (recalc(i, today()), i))
        .filter((i) => (!p.get('status') || i.status === p.get('status')) && (!p.get('studentId') || i.studentId === p.get('studentId'))
          && (!p.get('branchId') || i.branchId === p.get('branchId')) && (!p.get('termId') || i.termId === p.get('termId'))
          && (p.get('overdueOnly') !== 'true' || i.isOverdue))
        .sort((a, b) => (b.number ?? '~').localeCompare(a.number ?? '~'))
        .map(summary);
      return json(200, paginate(rows, url, (r) => [r.number, r.studentName, r.studentNumber]));
    }
    if (seg.length === 2 && method === 'POST') {
      const errors: Record<string, string[]> = {};
      const s = student(str(body.studentId));
      if (!s) errors.studentId = [body.studentId ? 'validation.invalid_value' : 'validation.required'];
      const lines = linesFrom(body.lines, errors);
      if (body.dueDate && !ISO.test(String(body.dueDate))) errors.dueDate = ['validation.invalid_date'];
      assertValid(errors);
      inScope('finance.invoices.manage', s!.branchId);
      const inv = newInvoice(s!.id, optStr(body.termId), optStr(body.dueDate), optStr(body.note), lines);
      f.invoices.push(inv);
      return json(201, inv);
    }
    if ((seg[2] === 'generate' || seg[2] === 'generate-bulk') && method === 'POST') {
      demand('finance.invoices.manage');
      const errors: Record<string, string[]> = {};
      const feeIds = Array.isArray(body.feeItemIds) ? (body.feeItemIds as string[]) : [];
      if (feeIds.length === 0) errors.feeItemIds = ['validation.required'];
      const items = feeIds.map((id) => f.feeItems.find((x) => x.id === id));
      if (items.some((x) => !x)) errors.feeItemIds = ['validation.invalid_value'];
      const enrollments = seg[2] === 'generate'
        ? data.academic.enrollments.filter((e) => e.id === body.enrollmentId)
        : data.academic.enrollments.filter((e) => {
          const cls = data.academic.classes.find((c) => c.id === e.classId);
          return !!cls && (body.classId ? cls.id === body.classId : cls.termId === body.termId);
        });
      if (seg[2] === 'generate' && enrollments.length === 0) errors.enrollmentId = ['validation.required'];
      if (seg[2] === 'generate-bulk' && !body.classId && !body.termId) errors.classId = ['validation.required'];
      assertValid(errors);
      let created = 0;
      let skipped = 0;
      const ids: string[] = [];
      for (const e of enrollments) {
        const cls = data.academic.classes.find((c) => c.id === e.classId)!;
        if (!includes(ctx.scope('finance.invoices.manage'), cls.branchId)) continue;
        if (e.status === 'withdrawn') { skipped++; continue; }
        const fresh = items.filter((i) => i && !f.charges[`${e.id}|${i.id}`] && (!i.termId || i.termId === cls.termId) && (!i.branchId || i.branchId === cls.branchId));
        if (fresh.length === 0) { skipped++; continue; }
        const lines = fresh.map((i) => ({ id: uuid(), feeItemId: i!.id, enrollmentId: e.id, description: i!.name, quantity: 1, unitPrice: i!.amount, amount: i!.amount, taxable: !!f.feeTypes.find((t) => t.id === i!.feeTypeId)?.taxable }));
        const inv = newInvoice(e.studentId, cls.termId, optStr(body.dueDate), null, lines);
        f.invoices.push(inv);
        fresh.forEach((i) => (f.charges[`${e.id}|${i!.id}`] = inv.id));
        created++;
        ids.push(inv.id);
      }
      return json(200, { created, skipped, invoiceIds: ids });
    }
    const inv = findInvoice(seg[2], method === 'GET' ? 'finance.read' : 'finance.invoices.manage');
    if (seg.length === 3 && method === 'GET') return json(200, inv);
    if (seg.length === 4 && seg[3] === 'pdf' && method === 'GET') return invoicePdf(inv);
    if (seg.length === 3 && method === 'PUT') {
      checkVersion(inv.rowVersion, body);
      if (inv.status !== 'draft') throw new HttpError(409, 'finance.invalid_state', { params: { status: inv.status } });
      const errors: Record<string, string[]> = {};
      const lines = linesFrom(body.lines, errors);
      assertValid(errors);
      Object.assign(inv, { termId: optStr(body.termId), dueDate: optStr(body.dueDate), note: optStr(body.note), lines, rowVersion: nv() });
      recalc(inv, today());
      return json(200, inv);
    }
    if (seg.length === 3 && method === 'DELETE') {
      if (inv.status !== 'draft') throw new HttpError(409, 'finance.invalid_state', { params: { status: inv.status } });
      f.invoices = f.invoices.filter((x) => x !== inv);
      for (const [k, v] of Object.entries(f.charges)) if (v === inv.id) delete f.charges[k];
      return json(204, null);
    }
    if (seg[3] === 'issue' && method === 'POST') {
      checkVersion(inv.rowVersion, body);
      if (inv.status !== 'draft') throw new HttpError(409, 'finance.invalid_state', { params: { status: inv.status } });
      if (inv.discounts.some((d) => d.status === 'pending')) throw new HttpError(422, 'finance.discount_pending');
      inv.taxRatePercent = f.settings.vatEnabled ? f.settings.vatRatePercent : 0;
      recalc(inv, today());
      if (inv.total <= 0) throw new HttpError(422, 'finance.invoice_empty');
      inv.number = fmtNo('INV', ++f.series.INV);
      inv.status = 'issued';
      inv.issueDate = optStr(body.issueDate) ?? today();
      inv.dueDate = optStr(body.dueDate) ?? inv.dueDate ?? inv.installments.at(-1)?.dueDate ?? addDays(inv.issueDate, 30);
      inv.issuedAt = nowIso();
      inv.rowVersion = nv();
      recalc(inv, today());
      return json(200, inv);
    }
    if (seg[3] === 'void' && method === 'POST') {
      checkVersion(inv.rowVersion, body);
      if (inv.status === 'draft' || inv.status === 'void') throw new HttpError(409, 'finance.invalid_state', { params: { status: inv.status } });
      if (!str(body.reason)) throw new HttpError(400, 'validation.failed', { errors: { reason: ['validation.required'] } });
      for (const p of f.payments) {
        const released = p.allocations.filter((a) => a.invoiceId === inv.id);
        if (released.length === 0) continue;
        p.allocations = p.allocations.filter((a) => a.invoiceId !== inv.id);
        p.allocated = p.allocations.reduce((s, a) => s + a.amount, 0);
        p.unallocated = p.amount - p.allocated - p.refunded;
      }
      for (const [k, v] of Object.entries(f.charges)) if (v === inv.id) delete f.charges[k];
      Object.assign(inv, { status: 'void', voidReason: str(body.reason), voidedAt: nowIso(), paidAmount: 0, rowVersion: nv() });
      inv.installments.forEach((i) => (i.paidAmount = 0));
      recalc(inv, today());
      return json(200, inv);
    }
    if (seg[3] === 'credit-notes' && method === 'POST') {
      checkVersion(inv.rowVersion, body);
      if (inv.status !== 'issued' && inv.status !== 'partiallyPaid') throw new HttpError(409, 'finance.invalid_state', { params: { status: inv.status } });
      const errors: Record<string, string[]> = {};
      const amount = amountField(errors, 'amount', body.amount);
      if (!str(body.reason)) errors.reason = ['validation.required'];
      assertValid(errors);
      if (amount! > inv.balance) throw new HttpError(422, 'finance.amount_exceeds_balance', { params: { balance: inv.balance } });
      const adj: InvoiceAdjustment = { id: uuid(), kind: 'creditNote', number: fmtNo('CN', ++f.series.CN), amount: amount!, reason: str(body.reason), installmentId: null, createdAt: nowIso() };
      inv.adjustments.push(adj);
      inv.rowVersion = nv();
      recalc(inv, today());
      return json(200, inv);
    }
    if (seg[3] === 'installments' && method === 'PUT') {
      checkVersion(inv.rowVersion, body);
      if (inv.status !== 'draft' && inv.status !== 'issued' && inv.status !== 'partiallyPaid') throw new HttpError(409, 'finance.invalid_state', { params: { status: inv.status } });
      if (inv.installments.some((i) => i.paidAmount > 0 || i.lateFeeCharged)) throw new HttpError(422, 'finance.installments_locked');
      const target = inv.status === 'draft' ? inv.total : inv.balance + inv.paidAmount;
      let plan: { dueDate: string; amount: number }[];
      if (Array.isArray(body.items)) {
        plan = (body.items as Record<string, unknown>[]).map((x) => ({ dueDate: String(x.dueDate ?? ''), amount: num(x.amount) ?? 0 }));
        const errors: Record<string, string[]> = {};
        plan.forEach((x, i) => {
          if (!ISO.test(x.dueDate)) errors[`items[${i}].dueDate`] = ['validation.invalid_date'];
          if (x.amount <= 0 || !Number.isInteger(x.amount)) errors[`items[${i}].amount`] = ['validation.invalid_value'];
        });
        if (plan.length === 0) errors.items = ['validation.required'];
        assertValid(errors);
      } else {
        const count = num(body.count) ?? 0;
        const first = String(body.firstDueDate ?? '');
        const interval = num(body.intervalDays) ?? 30;
        const errors: Record<string, string[]> = {};
        if (count < 1 || count > 24 || !Number.isInteger(count)) errors.count = ['validation.invalid_value'];
        if (!ISO.test(first)) errors.firstDueDate = ['validation.invalid_date'];
        if (interval < 1) errors.intervalDays = ['validation.invalid_value'];
        assertValid(errors);
        const base = Math.floor(target / count);
        plan = Array.from({ length: count }, (_, i) => ({ dueDate: addDays(first, i * interval), amount: i === count - 1 ? target - base * (count - 1) : base }));
      }
      if (plan.reduce((s, x) => s + x.amount, 0) !== target) throw new HttpError(422, 'finance.installments_mismatch', { params: { expected: target } });
      inv.installments = plan.sort((a, b) => a.dueDate.localeCompare(b.dueDate)).map<Installment>((x, i) => ({ id: uuid(), seqNo: i + 1, dueDate: x.dueDate, amount: x.amount, paidAmount: 0, lateFeeCharged: false, status: 'open' }));
      inv.dueDate = inv.installments.at(-1)!.dueDate;
      inv.rowVersion = nv();
      recalc(inv, today());
      return json(200, inv);
    }
    if (seg[3] === 'discounts' && method === 'POST') {
      if (inv.status !== 'draft') throw new HttpError(409, 'finance.invalid_state', { params: { status: inv.status } });
      const errors: Record<string, string[]> = {};
      const kind = ['discount', 'scholarship', 'sibling'].includes(String(body.kind)) ? (body.kind as InvoiceDiscount['kind']) : null;
      const type = ['percent', 'fixed'].includes(String(body.type)) ? (body.type as InvoiceDiscount['type']) : null;
      if (!kind) errors.kind = ['validation.required'];
      if (!type) errors.type = ['validation.required'];
      const value = num(body.value);
      if (value === null || value <= 0 || (type === 'percent' && value > 100) || (type === 'fixed' && !Number.isInteger(value))) errors.value = ['validation.invalid_value'];
      if (!str(body.reason)) errors.reason = ['validation.required'];
      assertValid(errors);
      const d: InvoiceDiscount = {
        id: uuid(), invoiceId: inv.id, kind: kind!, type: type!, value: value!, amount: 0, reason: str(body.reason), status: 'pending', requestedBy: user.id,
        requestedAt: nowIso(), decidedBy: null, decidedAt: null, decisionNote: null, rowVersion: nv(),
      };
      inv.discounts.push(d);
      inv.rowVersion = nv();
      return json(201, d);
    }
    throw notFound();
  }

  /* ----- approvals ----- */
  if (seg[1] === 'approvals' && method === 'GET') {
    const scope = demand('finance.read');
    return json(200, approvalItems(url.searchParams.get('status') ?? 'pending').filter((a) => includes(scope, a.branchId)));
  }
  if (seg[1] === 'discounts' && seg[2]) {
    const inv = f.invoices.find((i) => i.discounts.some((d) => d.id === seg[2]));
    const d = inv?.discounts.find((x) => x.id === seg[2]);
    if (!inv || !d) throw notFound();
    if (method === 'DELETE' && seg.length === 3) {
      if (d.requestedBy !== user.id) inScope('finance.approve', inv.branchId);
      else inScope('finance.invoices.manage', inv.branchId);
      if (d.status !== 'pending') throw new HttpError(409, 'finance.invalid_state', { params: { status: d.status } });
      inv.discounts = inv.discounts.filter((x) => x !== d);
      inv.rowVersion = nv();
      return json(204, null);
    }
    if ((seg[3] === 'approve' || seg[3] === 'reject') && method === 'POST') {
      inScope('finance.approve', inv.branchId);
      checkVersion(d.rowVersion, body);
      decideMaker(d.requestedBy);
      if (d.status !== 'pending') throw new HttpError(409, 'finance.invalid_state', { params: { status: d.status } });
      Object.assign(d, { status: seg[3] === 'approve' ? 'approved' : 'rejected', decidedBy: user.id, decidedAt: nowIso(), decisionNote: optStr(body.note), rowVersion: nv() });
      inv.rowVersion = nv();
      recalc(inv, today());
      return json(200, d);
    }
  }
  if (seg[1] === 'late-fees' && seg[2] === 'apply' && method === 'POST') {
    const scope = demand('finance.invoices.manage');
    if (!f.settings.lateFeeEnabled) return json(200, { applied: 0 });
    const asOf = optStr(body.asOf) ?? today();
    let applied = 0;
    for (const inv of f.invoices.filter((i) => includes(scope, i.branchId) && (i.status === 'issued' || i.status === 'partiallyPaid'))) {
      for (const i of inv.installments) {
        if (i.lateFeeCharged || i.paidAmount >= i.amount || daysBetween(i.dueDate, asOf) <= f.settings.lateFeeGraceDays) continue;
        const amount = f.settings.lateFeeType === 'percent' ? round0(((i.amount - i.paidAmount) * f.settings.lateFeeValue) / 100) : f.settings.lateFeeValue;
        if (amount <= 0) continue;
        i.lateFeeCharged = true;
        inv.adjustments.push({ id: uuid(), kind: 'lateFee', number: null, amount, reason: `late fee ${i.seqNo}`, installmentId: i.id, createdAt: nowIso() });
        applied++;
      }
      recalc(inv, today());
      inv.rowVersion = nv();
    }
    return json(200, { applied });
  }

  /* ----- payments ----- */
  if (seg[1] === 'payments') {
    if (seg.length === 2 && method === 'GET') {
      const scope = demand('finance.read');
      const p = url.searchParams;
      const rows = f.payments
        .filter((x) => includes(scope, x.branchId))
        .filter((x) => (!p.get('studentId') || x.studentId === p.get('studentId')) && (!p.get('method') || x.method === p.get('method'))
          && (!p.get('status') || x.status === p.get('status')) && (!p.get('from') || x.receivedOn >= p.get('from')!) && (!p.get('to') || x.receivedOn <= p.get('to')!)
          && (!p.get('branchId') || x.branchId === p.get('branchId')))
        .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
        .map(publicPayment);
      return json(200, paginate(rows, url, (r) => [r.receiptNumber, r.studentName, r.reference]));
    }
    if (seg.length === 2 && method === 'POST') {
      const errors: Record<string, string[]> = {};
      const s = student(str(body.studentId));
      if (!s) errors.studentId = [body.studentId ? 'validation.invalid_value' : 'validation.required'];
      const methodName = String(body.method ?? '');
      if (!['cash', 'pos', 'transfer', 'cheque'].includes(methodName)) errors.method = [body.method ? 'validation.invalid_value' : 'validation.required'];
      const amount = amountField(errors, 'amount', body.amount);
      const cheque = body.cheque as Record<string, unknown> | null | undefined;
      if (methodName === 'cheque') {
        if (!str(cheque?.number)) errors['cheque.number'] = ['validation.required'];
        if (!str(cheque?.bank)) errors['cheque.bank'] = ['validation.required'];
        if (!ISO.test(String(cheque?.dueDate ?? ''))) errors['cheque.dueDate'] = ['validation.required'];
      }
      if (body.receivedOn && !ISO.test(String(body.receivedOn))) errors.receivedOn = ['validation.invalid_date'];
      assertValid(errors);
      inScope('finance.payments.manage', s!.branchId);
      const key = optStr(body.idempotencyKey);
      const existing = key ? f.payments.find((x) => x.idempotencyKey === key) : undefined;
      if (existing) return json(200, publicPayment(existing));
      const invoiceId = optStr(body.invoiceId);
      if (invoiceId) {
        const target = f.invoices.find((i) => i.id === invoiceId && i.studentId === s!.id);
        if (!target) throw new HttpError(400, 'validation.failed', { errors: { invoiceId: ['validation.invalid_value'] } });
        if (target.status !== 'issued' && target.status !== 'partiallyPaid') throw new HttpError(409, 'finance.invalid_state', { params: { status: target.status } });
      }
      const p: MockFinance['payments'][number] = {
        id: uuid(), studentId: s!.id, studentName: `${s!.firstName} ${s!.lastName}`, branchId: s!.branchId, method: methodName as Payment['method'], status: 'pending',
        amount: amount!, currency: f.settings.currency, receivedOn: optStr(body.receivedOn) ?? today(), reference: optStr(body.reference),
        cheque: methodName === 'cheque' ? { number: str(cheque?.number), bank: str(cheque?.bank), dueDate: String(cheque?.dueDate), status: 'received' } : null,
        provider: null, providerRef: null, receiptNumber: null, allocated: 0, unallocated: 0, refunded: 0, failureCode: null, allocations: [], createdAt: nowIso(),
        rowVersion: nv(), idempotencyKey: key, authority: null, targetInvoiceId: invoiceId, targetInstallmentId: null, createdBy: user.id,
      };
      if (methodName !== 'cheque') succeed(f, p, { nextVersion: nv, today });
      f.payments.push(p);
      return json(201, publicPayment(p));
    }
    const p = findPayment(seg[2], method === 'GET' ? 'finance.read' : 'finance.payments.manage');
    if (seg.length === 3 && method === 'GET') return json(200, publicPayment(p));
    if (seg[3] === 'receipt' && seg[4] === 'pdf' && method === 'GET') return receiptPdf(p);
    if (seg[3] === 'cheque-status' && method === 'POST') {
      checkVersion(p.rowVersion, body);
      if (p.method !== 'cheque' || p.status !== 'pending') throw new HttpError(409, 'finance.invalid_state', { params: { status: p.status } });
      if (body.status === 'cleared') {
        p.cheque = { ...p.cheque!, status: 'cleared' };
        succeed(f, p, { nextVersion: nv, today });
      } else if (body.status === 'bounced') {
        p.cheque = { ...p.cheque!, status: 'bounced' };
        p.status = 'failed';
        p.failureCode = 'cheque_bounced';
      } else throw new HttpError(400, 'validation.failed', { errors: { status: ['validation.invalid_value'] } });
      p.rowVersion = nv();
      return json(200, publicPayment(p));
    }
    if (seg[3] === 'refunds' && method === 'POST') {
      const errors: Record<string, string[]> = {};
      const amount = amountField(errors, 'amount', body.amount);
      if (!str(body.reason)) errors.reason = ['validation.required'];
      assertValid(errors);
      const pendingRefunds = f.refunds.filter((r) => r.paymentId === p.id && r.status === 'pending').reduce((s, r) => s + r.amount, 0);
      if (p.status !== 'succeeded' || amount! > p.unallocated - pendingRefunds) throw new HttpError(422, 'finance.refund_exceeds_credit');
      const r: Refund = {
        id: uuid(), paymentId: p.id, studentId: p.studentId, branchId: p.branchId, amount: amount!, currency: p.currency, reason: str(body.reason), status: 'pending',
        requestedBy: user.id, requestedAt: nowIso(), decidedBy: null, decidedAt: null, decisionNote: null, rowVersion: nv(),
      };
      f.refunds.push(r);
      return json(201, r);
    }
    throw notFound();
  }
  if (seg[1] === 'refunds') {
    if (seg.length === 2 && method === 'GET') {
      const scope = demand('finance.read');
      const status = url.searchParams.get('status');
      return json(200, f.refunds.filter((r) => includes(scope, r.branchId) && (!status || r.status === status)));
    }
    const r = f.refunds.find((x) => x.id === seg[2]);
    if (!r) throw notFound();
    if ((seg[3] === 'approve' || seg[3] === 'reject') && method === 'POST') {
      inScope('finance.approve', r.branchId);
      checkVersion(r.rowVersion, body);
      decideMaker(r.requestedBy);
      if (r.status !== 'pending') throw new HttpError(409, 'finance.invalid_state', { params: { status: r.status } });
      Object.assign(r, { status: seg[3] === 'approve' ? 'approved' : 'rejected', decidedBy: user.id, decidedAt: nowIso(), decisionNote: optStr(body.note), rowVersion: nv() });
      if (seg[3] === 'approve') {
        const p = f.payments.find((x) => x.id === r.paymentId)!;
        p.refunded += r.amount;
        p.unallocated -= r.amount;
        p.rowVersion = nv();
      }
      return json(200, r);
    }
  }

  /* ----- student accounts ----- */
  if (seg[1] === 'students' && seg[2]) {
    const s = student(seg[2]);
    if (!s) throw notFound();
    if (seg[3] === 'account' && method === 'GET') {
      inScope('finance.read', s.branchId);
      return json(200, studentAccount(s.id, false));
    }
    if (seg[3] === 'apply-credit' && method === 'POST') {
      inScope('finance.invoices.manage', s.branchId);
      const inv = f.invoices.find((i) => i.id === body.invoiceId && i.studentId === s.id);
      if (!inv) throw new HttpError(400, 'validation.failed', { errors: { invoiceId: ['validation.invalid_value'] } });
      const credits = f.payments.filter((p) => p.studentId === s.id && p.status === 'succeeded' && p.unallocated > 0);
      if (credits.length === 0) throw new HttpError(422, 'finance.no_credit');
      for (const p of credits) allocate(f, p, p.unallocated, inv.id, { nextVersion: nv, today });
      recalc(inv, today());
      return json(200, inv);
    }
  }

  /* ----- reports ----- */
  if (seg[1] === 'reports' && method === 'GET') {
    const scope = demand('finance.reports.read');
    const p = url.searchParams;
    const csv = (p.get('format') ?? '').toLowerCase() === 'csv';
    const branchOk = (b: string) => includes(scope, b) && (!p.get('branchId') || b === p.get('branchId'));
    const open = f.invoices.filter((i) => branchOk(i.branchId) && (i.status === 'issued' || i.status === 'partiallyPaid')).map((i) => (recalc(i, today()), i));
    const csvFile = (name: string, header: string[], rows: (string | number | boolean | null)[][]) => {
      const esc = (v: string | number | boolean | null) => {
        const s = v === null ? '' : String(v);
        return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
      };
      const text = `\uFEFF${[header, ...rows].map((r) => r.map(esc).join(',')).join('\r\n')}\r\n`;
      return new Response(text, { status: 200, headers: { 'Content-Type': 'text/csv; charset=utf-8', 'Content-Disposition': `attachment; filename=${name}`, 'Cache-Control': 'no-store' } });
    };
    if (seg[2] === 'outstanding') {
      const rows = open.filter((i) => !p.get('termId') || i.termId === p.get('termId')).map((i) => ({
        invoiceId: i.id, number: i.number, studentId: i.studentId, studentName: i.studentName, studentNumber: i.studentNumber, branchId: i.branchId,
        dueDate: i.dueDate, total: i.total, paid: i.paidAmount, balance: i.balance, isOverdue: i.isOverdue,
      }));
      if (csv) return csvFile('outstanding.csv', ['number', 'studentNumber', 'studentName', 'dueDate', 'total', 'paid', 'balance', 'overdue'], rows.map((r) => [r.number, r.studentNumber, r.studentName, r.dueDate, r.total, r.paid, r.balance, r.isOverdue]));
      return json(200, rows);
    }
    if (seg[2] === 'aging') {
      const asOf = p.get('asOf') ?? today();
      const zero = () => ({ current: 0, d1_30: 0, d31_60: 0, d61_90: 0, d90plus: 0, total: 0 });
      const byStudent = new Map<string, { studentId: string; studentName: string; studentNumber: string; branchId: string; buckets: ReturnType<typeof zero> }>();
      const totals = zero();
      for (const inv of open) {
        const parts = inv.installments.length
          ? inv.installments.map((i) => ({ due: i.dueDate, amount: i.amount - i.paidAmount }))
          : [{ due: inv.dueDate ?? asOf, amount: inv.balance }];
        const row = byStudent.get(inv.studentId) ?? { studentId: inv.studentId, studentName: inv.studentName, studentNumber: inv.studentNumber, branchId: inv.branchId, buckets: zero() };
        for (const part of parts) {
          if (part.amount <= 0) continue;
          const late = daysBetween(part.due, asOf);
          const key = late <= 0 ? 'current' : late <= 30 ? 'd1_30' : late <= 60 ? 'd31_60' : late <= 90 ? 'd61_90' : 'd90plus';
          row.buckets[key] += part.amount;
          row.buckets.total += part.amount;
          totals[key] += part.amount;
          totals.total += part.amount;
        }
        byStudent.set(inv.studentId, row);
      }
      const rows = [...byStudent.values()];
      if (csv) return csvFile('aging.csv', ['studentNumber', 'studentName', 'current', 'd1_30', 'd31_60', 'd61_90', 'd90plus', 'total'], rows.map((r) => [r.studentNumber, r.studentName, r.buckets.current, r.buckets.d1_30, r.buckets.d31_60, r.buckets.d61_90, r.buckets.d90plus, r.buckets.total]));
      return json(200, { asOf, currency: f.settings.currency, buckets: totals, rows });
    }
    if (seg[2] === 'collections') {
      const groups = new Map<string, { date: string; branchId: string; method: string; count: number; amount: number }>();
      for (const x of f.payments.filter((y) => y.status === 'succeeded' && branchOk(y.branchId) && (!p.get('from') || y.receivedOn >= p.get('from')!) && (!p.get('to') || y.receivedOn <= p.get('to')!))) {
        const k = `${x.receivedOn}|${x.branchId}|${x.method}`;
        const g = groups.get(k) ?? { date: x.receivedOn, branchId: x.branchId, method: x.method, count: 0, amount: 0 };
        g.count++;
        g.amount += x.amount;
        groups.set(k, g);
      }
      const rows = [...groups.values()].sort((a, b) => a.date.localeCompare(b.date) || a.method.localeCompare(b.method));
      if (csv) return csvFile('collections.csv', ['date', 'branchId', 'method', 'count', 'amount'], rows.map((r) => [r.date, r.branchId, r.method, r.count, r.amount]));
      return json(200, rows);
    }
  }
  throw notFound();
}
