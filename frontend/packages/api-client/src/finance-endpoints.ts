import type { ReportCardPdfLanguage } from './academic-types';
import type { ApiClient } from './client';
import type {
  AgingReport,
  ApprovalItem,
  CollectionRow,
  DecisionRequest,
  DiscountRequest,
  FeeItem,
  FeeItemRequest,
  FeeType,
  FeeTypeRequest,
  FinanceReportKind,
  FinanceReportQuery,
  FinanceSettings,
  FinanceSettingsRequest,
  GenerateBulkRequest,
  GenerateRequest,
  GenerateResult,
  InstallmentsRequest,
  Invoice,
  InvoiceDiscount,
  InvoiceListQuery,
  InvoiceRequest,
  InvoiceSummary,
  OutstandingRow,
  Payment,
  PaymentListQuery,
  PaymentRequest,
  PaymentStatusResult,
  PayOnlineRequest,
  PayOnlineResult,
  Refund,
  StudentAccount,
} from './finance-types';
import type { Guid, PageQuery, Paged, RowVersion } from './types';

const id = (value: Guid) => encodeURIComponent(value);
const page = (query: PageQuery = {}) => ({ page: query.page, pageSize: query.pageSize, search: query.search });

/** Typed endpoints for the Milestone 3 finance contract (paths relative to /api/v1). */
export function createFinanceEndpoints(client: ApiClient) {
  return {
    settings: {
      get: () => client.get<FinanceSettings>('/finance/settings'),
      save: (body: FinanceSettingsRequest) => client.put<FinanceSettings>('/finance/settings', body),
    },
    feeTypes: {
      list: () => client.get<FeeType[]>('/finance/fee-types'),
      create: (body: FeeTypeRequest) => client.post<FeeType>('/finance/fee-types', body),
      update: (feeTypeId: Guid, body: FeeTypeRequest) => client.put<FeeType>(`/finance/fee-types/${id(feeTypeId)}`, body),
      remove: (feeTypeId: Guid) => client.delete(`/finance/fee-types/${id(feeTypeId)}`),
    },
    feeItems: {
      list: (query: { termId?: Guid; feeTypeId?: Guid; activeOnly?: boolean } = {}) => client.get<FeeItem[]>('/finance/fee-items', { query }),
      create: (body: FeeItemRequest) => client.post<FeeItem>('/finance/fee-items', body),
      update: (feeItemId: Guid, body: FeeItemRequest) => client.put<FeeItem>(`/finance/fee-items/${id(feeItemId)}`, body),
      remove: (feeItemId: Guid) => client.delete(`/finance/fee-items/${id(feeItemId)}`),
    },
    invoices: {
      list: (query: PageQuery & InvoiceListQuery = {}) =>
        client.get<Paged<InvoiceSummary>>('/finance/invoices', {
          query: { ...page(query), status: query.status, studentId: query.studentId, branchId: query.branchId, termId: query.termId, overdueOnly: query.overdueOnly },
        }),
      get: (invoiceId: Guid) => client.get<Invoice>(`/finance/invoices/${id(invoiceId)}`),
      create: (body: InvoiceRequest) => client.post<Invoice>('/finance/invoices', body),
      update: (invoiceId: Guid, body: InvoiceRequest & { rowVersion: RowVersion }) => client.put<Invoice>(`/finance/invoices/${id(invoiceId)}`, body),
      remove: (invoiceId: Guid) => client.delete(`/finance/invoices/${id(invoiceId)}`),
      issue: (invoiceId: Guid, body: { rowVersion: RowVersion; issueDate?: string | null; dueDate?: string | null }) =>
        client.post<Invoice>(`/finance/invoices/${id(invoiceId)}/issue`, body),
      void: (invoiceId: Guid, body: { rowVersion: RowVersion; reason: string }) => client.post<Invoice>(`/finance/invoices/${id(invoiceId)}/void`, body),
      creditNote: (invoiceId: Guid, body: { rowVersion: RowVersion; amount: number; reason: string }) =>
        client.post<Invoice>(`/finance/invoices/${id(invoiceId)}/credit-notes`, body),
      setInstallments: (invoiceId: Guid, body: InstallmentsRequest) => client.put<Invoice>(`/finance/invoices/${id(invoiceId)}/installments`, body),
      requestDiscount: (invoiceId: Guid, body: DiscountRequest) => client.post<InvoiceDiscount>(`/finance/invoices/${id(invoiceId)}/discounts`, body),
      generate: (body: GenerateRequest) => client.post<GenerateResult>('/finance/invoices/generate', body),
      generateBulk: (body: GenerateBulkRequest) => client.post<GenerateResult>('/finance/invoices/generate-bulk', body),
      /** Issued/paid/void only (draft → 422 finance.invoice_not_issued). */
      pdf: (invoiceId: Guid, lang?: ReportCardPdfLanguage) => client.download(`/finance/invoices/${id(invoiceId)}/pdf`, { query: { lang } }),
    },
    discounts: {
      approve: (discountId: Guid, body: DecisionRequest) => client.post<InvoiceDiscount>(`/finance/discounts/${id(discountId)}/approve`, body),
      reject: (discountId: Guid, body: DecisionRequest) => client.post<InvoiceDiscount>(`/finance/discounts/${id(discountId)}/reject`, body),
      remove: (discountId: Guid) => client.delete(`/finance/discounts/${id(discountId)}`),
    },
    approvals: {
      list: (status = 'pending') => client.get<ApprovalItem[]>('/finance/approvals', { query: { status } }),
    },
    lateFees: {
      apply: (asOf?: string) => client.post<{ applied: number }>('/finance/late-fees/apply', { asOf: asOf || null }),
    },
    payments: {
      list: (query: PageQuery & PaymentListQuery = {}) =>
        client.get<Paged<Payment>>('/finance/payments', {
          query: { ...page(query), studentId: query.studentId, method: query.method, status: query.status, from: query.from, to: query.to, branchId: query.branchId },
        }),
      get: (paymentId: Guid) => client.get<Payment>(`/finance/payments/${id(paymentId)}`),
      record: (body: PaymentRequest) => client.post<Payment>('/finance/payments', body),
      chequeStatus: (paymentId: Guid, body: { rowVersion: RowVersion; status: 'cleared' | 'bounced' }) =>
        client.post<Payment>(`/finance/payments/${id(paymentId)}/cheque-status`, body),
      requestRefund: (paymentId: Guid, body: { amount: number; reason: string }) => client.post<Refund>(`/finance/payments/${id(paymentId)}/refunds`, body),
      receiptPdf: (paymentId: Guid, lang?: ReportCardPdfLanguage) => client.download(`/finance/payments/${id(paymentId)}/receipt/pdf`, { query: { lang } }),
    },
    refunds: {
      list: (status?: string) => client.get<Refund[]>('/finance/refunds', { query: { status } }),
      approve: (refundId: Guid, body: DecisionRequest) => client.post<Refund>(`/finance/refunds/${id(refundId)}/approve`, body),
      reject: (refundId: Guid, body: DecisionRequest) => client.post<Refund>(`/finance/refunds/${id(refundId)}/reject`, body),
    },
    students: {
      account: (studentId: Guid) => client.get<StudentAccount>(`/finance/students/${id(studentId)}/account`),
      applyCredit: (studentId: Guid, invoiceId: Guid) => client.post<Invoice>(`/finance/students/${id(studentId)}/apply-credit`, { invoiceId }),
    },
    reports: {
      outstanding: (query: FinanceReportQuery = {}) =>
        client.get<OutstandingRow[]>('/finance/reports/outstanding', { query: { branchId: query.branchId, termId: query.termId } }),
      aging: (query: FinanceReportQuery = {}) => client.get<AgingReport>('/finance/reports/aging', { query: { asOf: query.asOf, branchId: query.branchId } }),
      collections: (query: FinanceReportQuery = {}) =>
        client.get<CollectionRow[]>('/finance/reports/collections', { query: { from: query.from, to: query.to, branchId: query.branchId } }),
      /** The same report as CSV (UTF-8 with BOM), downloaded through the BFF. */
      csv: (kind: FinanceReportKind, query: FinanceReportQuery = {}) =>
        client.download(`/finance/reports/${kind}`, { query: { ...query, format: 'csv' } }),
    },
    portal: {
      account: (studentId: Guid) => client.get<StudentAccount>(`/portal/me/finance/students/${id(studentId)}/account`),
      invoice: (invoiceId: Guid) => client.get<Invoice>(`/portal/me/finance/invoices/${id(invoiceId)}`),
      invoicePdf: (invoiceId: Guid, lang?: ReportCardPdfLanguage) => client.download(`/portal/me/finance/invoices/${id(invoiceId)}/pdf`, { query: { lang } }),
      pay: (invoiceId: Guid, body: PayOnlineRequest) => client.post<PayOnlineResult>(`/portal/me/finance/invoices/${id(invoiceId)}/pay`, body),
      payment: (paymentId: Guid) => client.get<Payment>(`/portal/me/finance/payments/${id(paymentId)}`),
      verify: (paymentId: Guid) => client.post<PaymentStatusResult>(`/portal/me/finance/payments/${id(paymentId)}/verify`, {}),
      receiptPdf: (paymentId: Guid, lang?: ReportCardPdfLanguage) =>
        client.download(`/portal/me/finance/payments/${id(paymentId)}/receipt/pdf`, { query: { lang } }),
    },
    /**
     * Development fake gateway only: reports the payer's choice to the anonymous provider callback (the same URL a real
     * provider redirects to). The server binds it to the stored authority and verifies with the stored amount.
     */
    fakeGatewayCallback: (paymentId: Guid, authority: string, status: 'OK' | 'NOK') =>
      client.get<PaymentStatusResult>(`/finance/gateway/fake/callback/${id(paymentId)}`, { query: { Authority: authority, Status: status } }),
  };
}

export type FinanceEndpoints = ReturnType<typeof createFinanceEndpoints>;
