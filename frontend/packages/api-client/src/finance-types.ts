/**
 * DTOs for the Milestone 3 institute finance contract (EEMP-014, work/eemp-milestone3-contract.md and
 * backend Finance.Application/Dtos.cs). Amounts are JSON numbers in the record's `currency` (ISO 4217), already
 * rounded by the server to the currency's minor unit (IRR: whole rials). The client never computes money it sends
 * back except user-entered amounts, which the server validates and rounds.
 */
import type { Guid, IsoDate, RowVersion, UtcInstant } from './types';

export type Money = number;

export const INVOICE_STATUSES = ['draft', 'issued', 'partiallyPaid', 'paid', 'void'] as const;
export type InvoiceStatus = (typeof INVOICE_STATUSES)[number];
export const PAYMENT_METHODS = ['cash', 'pos', 'transfer', 'cheque', 'online'] as const;
export type PaymentMethod = (typeof PAYMENT_METHODS)[number];
/** Methods an officer records by hand (online payments come only from the gateway). */
export const MANUAL_PAYMENT_METHODS = ['cash', 'pos', 'transfer', 'cheque'] as const;
export const PAYMENT_STATUSES = ['pending', 'succeeded', 'failed', 'cancelled'] as const;
export type PaymentStatus = (typeof PAYMENT_STATUSES)[number];
export type ChequeStatus = 'received' | 'cleared' | 'bounced';
export type DiscountKind = 'discount' | 'scholarship' | 'sibling';
export type DiscountType = 'percent' | 'fixed';
export type ApprovalStatus = 'pending' | 'approved' | 'rejected';
export type InstallmentStatus = 'open' | 'paid' | 'overdue';
export type LateFeeType = 'fixed' | 'percent';

export interface FinanceSettings {
  currency: string;
  vatEnabled: boolean;
  vatRatePercent: number;
  lateFeeEnabled: boolean;
  lateFeeType: LateFeeType;
  lateFeeValue: number;
  lateFeeGraceDays: number;
  siblingDiscountPercent: number;
  blockResultsOnOverdue: boolean;
  onlinePaymentsEnabled: boolean;
  /** Gateways enabled by server configuration (e.g. ["fake"], ["zarinpal"]); read-only. */
  availableGateways: string[];
  rowVersion: RowVersion;
}
export type FinanceSettingsRequest = Omit<FinanceSettings, 'currency' | 'availableGateways'>;

export interface FeeType {
  id: Guid;
  code: string;
  name: string;
  taxable: boolean;
  isActive: boolean;
  rowVersion: RowVersion;
}
export interface FeeTypeRequest {
  code: string;
  name: string;
  taxable: boolean;
  isActive?: boolean;
  rowVersion?: RowVersion;
}

export interface FeeItem {
  id: Guid;
  feeTypeId: Guid;
  feeTypeCode: string;
  name: string;
  amount: Money;
  currency: string;
  termId: Guid | null;
  courseId: Guid | null;
  branchId: Guid | null;
  isActive: boolean;
  rowVersion: RowVersion;
}
export interface FeeItemRequest {
  feeTypeId: Guid;
  name: string;
  amount: Money;
  termId?: Guid | null;
  courseId?: Guid | null;
  branchId?: Guid | null;
  isActive?: boolean;
  rowVersion?: RowVersion;
}

export interface InvoiceLine {
  id: Guid;
  feeItemId: Guid | null;
  enrollmentId: Guid | null;
  description: string;
  quantity: number;
  unitPrice: Money;
  amount: Money;
  taxable: boolean;
}
export interface InvoiceDiscount {
  id: Guid;
  invoiceId: Guid;
  kind: DiscountKind;
  type: DiscountType;
  value: number;
  amount: Money;
  reason: string;
  status: ApprovalStatus;
  requestedBy: Guid | null;
  requestedAt: UtcInstant;
  decidedBy: Guid | null;
  decidedAt: UtcInstant | null;
  decisionNote: string | null;
  rowVersion: RowVersion;
}
export interface Installment {
  id: Guid;
  seqNo: number;
  dueDate: IsoDate;
  amount: Money;
  paidAmount: Money;
  lateFeeCharged: boolean;
  status: InstallmentStatus;
}
export interface InvoiceAdjustment {
  id: Guid;
  kind: 'creditNote' | 'lateFee';
  number: string | null;
  amount: Money;
  reason: string;
  installmentId: Guid | null;
  createdAt: UtcInstant;
}

export interface InvoiceSummary {
  id: Guid;
  number: string | null;
  studentId: Guid;
  studentName: string;
  studentNumber: string;
  branchId: Guid;
  termId: Guid | null;
  status: InvoiceStatus;
  currency: string;
  issueDate: IsoDate | null;
  dueDate: IsoDate | null;
  total: Money;
  paidAmount: Money;
  balance: Money;
  isOverdue: boolean;
  rowVersion: RowVersion;
}
export interface Invoice extends InvoiceSummary {
  subtotal: Money;
  discountTotal: Money;
  taxRatePercent: number;
  taxTotal: Money;
  lateFeeTotal: Money;
  creditTotal: Money;
  note: string | null;
  voidReason: string | null;
  issuedAt: UtcInstant | null;
  voidedAt: UtcInstant | null;
  lines: InvoiceLine[];
  discounts: InvoiceDiscount[];
  installments: Installment[];
  adjustments: InvoiceAdjustment[];
}

export interface InvoiceListQuery {
  status?: string;
  studentId?: Guid;
  branchId?: Guid;
  termId?: Guid;
  overdueOnly?: boolean;
}
export interface InvoiceLineRequest {
  feeItemId?: Guid | null;
  description?: string | null;
  quantity: number;
  unitPrice?: Money | null;
}
export interface InvoiceRequest {
  studentId: Guid;
  termId?: Guid | null;
  dueDate?: IsoDate | null;
  note?: string | null;
  lines: InvoiceLineRequest[];
  rowVersion?: RowVersion;
}
export interface GenerateRequest {
  enrollmentId: Guid;
  feeItemIds: Guid[];
  dueDate?: IsoDate | null;
}
export interface GenerateBulkRequest {
  classId?: Guid | null;
  termId?: Guid | null;
  feeItemIds: Guid[];
  dueDate?: IsoDate | null;
}
export interface GenerateResult {
  created: number;
  skipped: number;
  invoiceIds: Guid[];
}
export interface DiscountRequest {
  kind: DiscountKind;
  type: DiscountType;
  value: number;
  reason: string;
}
export type InstallmentsRequest =
  | { rowVersion: RowVersion; items: { dueDate: IsoDate; amount: Money }[] }
  | { rowVersion: RowVersion; count: number; firstDueDate: IsoDate; intervalDays: number };

export interface Cheque {
  number: string | null;
  bank: string | null;
  dueDate: IsoDate | null;
  status: ChequeStatus | null;
}
export interface PaymentAllocation {
  invoiceId: Guid;
  invoiceNumber: string | null;
  installmentId: Guid | null;
  amount: Money;
}
export interface Payment {
  id: Guid;
  studentId: Guid;
  studentName: string;
  branchId: Guid;
  method: PaymentMethod;
  status: PaymentStatus;
  amount: Money;
  currency: string;
  receivedOn: IsoDate;
  reference: string | null;
  cheque: Cheque | null;
  provider: string | null;
  providerRef: string | null;
  receiptNumber: string | null;
  allocated: Money;
  unallocated: Money;
  refunded: Money;
  failureCode: string | null;
  allocations: PaymentAllocation[];
  createdAt: UtcInstant;
  rowVersion: RowVersion;
}
export interface PaymentRequest {
  studentId: Guid;
  method: PaymentMethod;
  amount: Money;
  receivedOn?: IsoDate | null;
  reference?: string | null;
  invoiceId?: Guid | null;
  cheque?: { number: string; bank: string; dueDate: IsoDate } | null;
  /** Client-generated per form submission: a retried submit returns the same payment. */
  idempotencyKey: string;
}
export interface PaymentListQuery {
  studentId?: Guid;
  method?: string;
  status?: string;
  from?: IsoDate;
  to?: IsoDate;
  branchId?: Guid;
}
export interface StudentAccount {
  studentId: Guid;
  studentName: string;
  studentNumber: string;
  currency: string;
  totalInvoiced: Money;
  totalPaid: Money;
  balance: Money;
  creditBalance: Money;
  overdue: Money;
  invoices: InvoiceSummary[];
  payments: Payment[];
}

export interface Refund {
  id: Guid;
  paymentId: Guid;
  studentId: Guid;
  branchId: Guid;
  amount: Money;
  currency: string;
  reason: string;
  status: ApprovalStatus;
  requestedBy: Guid | null;
  requestedAt: UtcInstant;
  decidedBy: Guid | null;
  decidedAt: UtcInstant | null;
  decisionNote: string | null;
  rowVersion: RowVersion;
}
export interface ApprovalItem {
  type: 'discount' | 'refund';
  id: Guid;
  invoiceId: Guid | null;
  paymentId: Guid | null;
  studentId: Guid;
  studentName: string;
  branchId: Guid;
  amount: Money;
  currency: string;
  /** Server-composed detail (e.g. "scholarship 10%" or an invoice/receipt number); shown as data, not UI text. */
  detail: string;
  reason: string;
  requestedBy: Guid | null;
  requestedAt: UtcInstant;
  rowVersion: RowVersion;
}
export interface DecisionRequest {
  rowVersion: RowVersion;
  note?: string | null;
}

export interface PayOnlineRequest {
  installmentId?: Guid | null;
  amount?: Money | null;
  idempotencyKey: string;
}
export interface PayOnlineResult {
  paymentId: Guid;
  redirectUrl: string;
  provider: string;
}
export interface PaymentStatusResult {
  paymentId: Guid;
  status: PaymentStatus;
  receiptNumber: string | null;
  failureCode: string | null;
}

export interface OutstandingRow {
  invoiceId: Guid;
  number: string | null;
  studentId: Guid;
  studentName: string;
  studentNumber: string;
  branchId: Guid;
  dueDate: IsoDate | null;
  total: Money;
  paid: Money;
  balance: Money;
  isOverdue: boolean;
}
export interface AgingBuckets {
  current: Money;
  d1_30: Money;
  d31_60: Money;
  d61_90: Money;
  d90plus: Money;
  total: Money;
}
export const AGING_BUCKETS = ['current', 'd1_30', 'd31_60', 'd61_90', 'd90plus'] as const;
export interface AgingRow {
  studentId: Guid;
  studentName: string;
  studentNumber: string;
  branchId: Guid;
  buckets: AgingBuckets;
}
export interface AgingReport {
  asOf: IsoDate;
  currency: string;
  buckets: AgingBuckets;
  rows: AgingRow[];
}
export interface CollectionRow {
  date: IsoDate;
  branchId: Guid;
  method: PaymentMethod;
  count: number;
  amount: Money;
}
export type FinanceReportKind = 'outstanding' | 'aging' | 'collections';
export interface FinanceReportQuery {
  branchId?: Guid;
  termId?: Guid;
  asOf?: IsoDate;
  from?: IsoDate;
  to?: IsoDate;
}
