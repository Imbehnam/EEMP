namespace EEMP.Modules.Finance.Application;

// Settings and fee setup
public sealed record FinanceSettingsDto(string Currency, bool VatEnabled, decimal VatRatePercent, bool LateFeeEnabled, string LateFeeType,
    decimal LateFeeValue, int LateFeeGraceDays, decimal SiblingDiscountPercent, bool BlockResultsOnOverdue, bool OnlinePaymentsEnabled,
    IReadOnlyList<string> AvailableGateways, string RowVersion);

public sealed record FinanceSettingsRequest(bool VatEnabled, decimal? VatRatePercent, bool LateFeeEnabled, string? LateFeeType, decimal? LateFeeValue,
    int? LateFeeGraceDays, decimal? SiblingDiscountPercent, bool BlockResultsOnOverdue, bool OnlinePaymentsEnabled, string? RowVersion);

public sealed record FeeTypeDto(Guid Id, string Code, string Name, bool Taxable, bool IsActive, string RowVersion);
public sealed record FeeTypeRequest(string? Code, string? Name, bool Taxable, bool? IsActive, string? RowVersion);

public sealed record FeeItemDto(Guid Id, Guid FeeTypeId, string FeeTypeCode, string Name, decimal Amount, string Currency, Guid? TermId, Guid? CourseId,
    Guid? BranchId, bool IsActive, string RowVersion);
public sealed record FeeItemRequest(Guid? FeeTypeId, string? Name, decimal? Amount, Guid? TermId, Guid? CourseId, Guid? BranchId, bool? IsActive, string? RowVersion);

// Invoices
public sealed record InvoiceLineDto(Guid Id, Guid? FeeItemId, Guid? EnrollmentId, string Description, int Quantity, decimal UnitPrice, decimal Amount, bool Taxable);
public sealed record DiscountDto(Guid Id, Guid InvoiceId, string Kind, string Type, decimal Value, decimal Amount, string Reason, string Status,
    Guid? RequestedBy, DateTime RequestedAt, Guid? DecidedBy, DateTime? DecidedAt, string? DecisionNote, string RowVersion, Guid? WorkflowInstanceId = null);
public sealed record InstallmentDto(Guid Id, int SeqNo, string DueDate, decimal Amount, decimal PaidAmount, bool LateFeeCharged, string Status);
public sealed record AdjustmentDto(Guid Id, string Kind, string? Number, decimal Amount, string Reason, Guid? InstallmentId, DateTime CreatedAt);

public sealed record InvoiceDto(Guid Id, string? Number, Guid StudentId, string StudentName, string StudentNumber, Guid BranchId, Guid? TermId, string Status,
    string Currency, string? IssueDate, string? DueDate, decimal Subtotal, decimal DiscountTotal, decimal TaxRatePercent, decimal TaxTotal,
    decimal LateFeeTotal, decimal CreditTotal, decimal Total, decimal PaidAmount, decimal Balance, bool IsOverdue, string? Note, string? VoidReason,
    DateTime? IssuedAt, DateTime? VoidedAt, IReadOnlyList<InvoiceLineDto> Lines, IReadOnlyList<DiscountDto> Discounts,
    IReadOnlyList<InstallmentDto> Installments, IReadOnlyList<AdjustmentDto> Adjustments, string RowVersion);

public sealed record InvoiceSummaryDto(Guid Id, string? Number, Guid StudentId, string StudentName, string StudentNumber, Guid BranchId, Guid? TermId,
    string Status, string Currency, string? IssueDate, string? DueDate, decimal Total, decimal PaidAmount, decimal Balance, bool IsOverdue, string RowVersion);

public sealed record InvoiceLineRequest(Guid? FeeItemId, string? Description, int? Quantity, decimal? UnitPrice);
public sealed record InvoiceRequest(Guid? StudentId, Guid? TermId, string? DueDate, string? Note, IReadOnlyList<InvoiceLineRequest>? Lines, string? RowVersion);
public sealed record InvoiceListQuery(string? Status, Guid? StudentId, Guid? BranchId, Guid? TermId, bool? OverdueOnly);
public sealed record IssueRequest(string? RowVersion, string? IssueDate, string? DueDate);
public sealed record VoidRequest(string? RowVersion, string? Reason);
public sealed record CreditNoteRequest(string? RowVersion, decimal? Amount, string? Reason);
public sealed record GenerateRequest(Guid? EnrollmentId, IReadOnlyList<Guid>? FeeItemIds, string? DueDate);
public sealed record GenerateBulkRequest(Guid? ClassId, Guid? TermId, IReadOnlyList<Guid>? FeeItemIds, string? DueDate);
public sealed record GenerateResult(int Created, int Skipped, IReadOnlyList<Guid> InvoiceIds);
public sealed record DiscountRequest(string? Kind, string? Type, decimal? Value, string? Reason);
/// <summary>Edit a returned discount and resubmit it to its workflow (DL-081).</summary>
public sealed record DiscountResubmitRequest(string? Kind, string? Type, decimal? Value, string? Reason, string? RowVersion);
/// <summary>Edit a returned refund and resubmit it to its workflow (DL-081).</summary>
public sealed record RefundResubmitRequest(decimal? Amount, string? Reason, string? RowVersion);
public sealed record DecisionRequest(string? RowVersion, string? Note);
public sealed record InstallmentItemRequest(string? DueDate, decimal? Amount);
public sealed record InstallmentsRequest(string? RowVersion, IReadOnlyList<InstallmentItemRequest>? Items, int? Count, string? FirstDueDate, int? IntervalDays);
public sealed record LateFeeRequest(string? AsOf);
public sealed record LateFeeResult(int Applied);

// Payments
public sealed record ChequeDto(string? Number, string? Bank, string? DueDate, string? Status);
public sealed record AllocationDto(Guid InvoiceId, string? InvoiceNumber, Guid? InstallmentId, decimal Amount);
public sealed record PaymentDto(Guid Id, Guid StudentId, string StudentName, Guid BranchId, string Method, string Status, decimal Amount, string Currency,
    string ReceivedOn, string? Reference, ChequeDto? Cheque, string? Provider, string? ProviderRef, string? ReceiptNumber, decimal Allocated,
    decimal Unallocated, decimal Refunded, string? FailureCode, IReadOnlyList<AllocationDto> Allocations, DateTime CreatedAt, string RowVersion);
public sealed record ChequeRequest(string? Number, string? Bank, string? DueDate);
public sealed record PaymentRequest(Guid? StudentId, string? Method, decimal? Amount, string? ReceivedOn, string? Reference, Guid? InvoiceId,
    ChequeRequest? Cheque, string? IdempotencyKey);
public sealed record ChequeStatusRequest(string? RowVersion, string? Status);
public sealed record PaymentListQuery(Guid? StudentId, string? Method, string? Status, string? From, string? To, Guid? BranchId);
public sealed record ApplyCreditRequest(Guid? InvoiceId);
public sealed record StudentAccountDto(Guid StudentId, string StudentName, string StudentNumber, string Currency, decimal TotalInvoiced, decimal TotalPaid,
    decimal Balance, decimal CreditBalance, decimal Overdue, IReadOnlyList<InvoiceSummaryDto> Invoices, IReadOnlyList<PaymentDto> Payments);

// Refunds and approvals
public sealed record RefundRequest(decimal? Amount, string? Reason);
public sealed record RefundDto(Guid Id, Guid PaymentId, Guid StudentId, Guid BranchId, decimal Amount, string Currency, string Reason, string Status,
    Guid? RequestedBy, DateTime RequestedAt, Guid? DecidedBy, DateTime? DecidedAt, string? DecisionNote, string RowVersion, Guid? WorkflowInstanceId = null);
public sealed record ApprovalItemDto(string Type, Guid Id, Guid? InvoiceId, Guid? PaymentId, Guid StudentId, string StudentName, Guid BranchId,
    decimal Amount, string Currency, string Detail, string Reason, Guid? RequestedBy, DateTime RequestedAt, string RowVersion, Guid? WorkflowInstanceId = null);

// Online payments
public sealed record PayOnlineRequest(Guid? InstallmentId, decimal? Amount, string? IdempotencyKey);
public sealed record PayOnlineResult(Guid PaymentId, string RedirectUrl, string Provider);
public sealed record PaymentStatusDto(Guid PaymentId, string Status, string? ReceiptNumber, string? FailureCode);

// Reports
public sealed record OutstandingRow(Guid InvoiceId, string? Number, Guid StudentId, string StudentName, string StudentNumber, Guid BranchId,
    string? DueDate, decimal Total, decimal Paid, decimal Balance, bool IsOverdue);
public sealed record AgingBuckets(decimal Current, decimal D1_30, decimal D31_60, decimal D61_90, decimal D90plus, decimal Total);
public sealed record AgingRow(Guid StudentId, string StudentName, string StudentNumber, Guid BranchId, AgingBuckets Buckets);
public sealed record AgingReport(string AsOf, string Currency, AgingBuckets Buckets, IReadOnlyList<AgingRow> Rows);
public sealed record CollectionRow(string Date, Guid BranchId, string Method, int Count, decimal Amount);
