using EEMP.SharedKernel.Domain;

namespace EEMP.Modules.Finance.Domain;

public enum InvoiceStatus { Draft, Issued, PartiallyPaid, Paid, Void }
public enum DiscountKind { Discount, Scholarship, Sibling }
public enum DiscountType { Percent, Fixed }
/// <summary>Returned: the workflow approver sent the request back to the maker, who may edit and resubmit or cancel it (DL-081).</summary>
public enum ApprovalStatus { Pending, Approved, Rejected, Returned }
public enum PaymentMethod { Cash, Pos, Transfer, Cheque, Online }
public enum PaymentStatus { Pending, Succeeded, Failed, Cancelled }
public enum ChequeStatus { Received, Cleared, Bounced }
public enum AdjustmentKind { CreditNote, LateFee }
public enum LateFeeType { Fixed, Percent }

/// <summary>One row per tenant (created on first use). Currency is not here: it is the tenant's (DL-059).</summary>
public sealed class FinanceSettings : TenantEntity
{
    public bool VatEnabled { get; set; }
    public decimal VatRatePercent { get; set; }
    public bool LateFeeEnabled { get; set; }
    public LateFeeType LateFeeType { get; set; } = LateFeeType.Fixed;
    public decimal LateFeeValue { get; set; }
    public int LateFeeGraceDays { get; set; }
    public decimal SiblingDiscountPercent { get; set; }
    public bool BlockResultsOnOverdue { get; set; }
    public bool OnlinePaymentsEnabled { get; set; }
}

public sealed class FeeType : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Taxable { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A priced fee. Null TermId/CourseId/BranchId = applies to any.</summary>
public sealed class FeeItem : TenantEntity
{
    public Guid FeeTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "IRR";
    public Guid? TermId { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? BranchId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Invoice : TenantEntity
{
    public string? Number { get; set; }
    public Guid StudentId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? TermId { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public string Currency { get; set; } = "IRR";
    public DateOnly? IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public decimal LateFeeTotal { get; set; }
    public decimal CreditTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public string? Note { get; set; }
    public string? VoidReason { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
    public List<InvoiceDiscount> Discounts { get; set; } = [];
    public List<Installment> Installments { get; set; } = [];
    public List<InvoiceAdjustment> Adjustments { get; set; } = [];

    /// <summary>What is still owed: total + late fees − credit notes − payments.</summary>
    public decimal Balance => Status is InvoiceStatus.Draft or InvoiceStatus.Void ? 0 : Total + LateFeeTotal - CreditTotal - PaidAmount;

    public bool IsOpen => Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid;
}

public sealed class InvoiceLine : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid? FeeItemId { get; set; }
    public Guid? EnrollmentId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public bool Taxable { get; set; }
    public int SortOrder { get; set; }
}

public sealed class InvoiceDiscount : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public DiscountKind Kind { get; set; }
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    /// <summary>Money amount computed when approved (0 while pending/rejected).</summary>
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public Guid? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    /// <summary>The EEMP-011 workflow instance deciding this request (DL-081); null for requests decided by the local M3 maker-checker.</summary>
    public Guid? WorkflowInstanceId { get; set; }
    public bool IsOpen => Status is ApprovalStatus.Pending or ApprovalStatus.Returned;
}

public sealed class Installment : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }
    public int SeqNo { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public bool LateFeeCharged { get; set; }
    public decimal Remaining => Amount - PaidAmount;
}

/// <summary>Credit notes reduce, late fees increase what is owed; an issued invoice itself is never edited.</summary>
public sealed class InvoiceAdjustment : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }
    public AdjustmentKind Kind { get; set; }
    public string? Number { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? InstallmentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

/// <summary>Idempotency guard: one (enrollment, fee item) charge is generated at most once while its invoice is not void.</summary>
public sealed class GeneratedCharge : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid FeeItemId { get; set; }
    public Guid InvoiceId { get; set; }
}

public sealed class Payment : TenantEntity
{
    public Guid StudentId { get; set; }
    public Guid BranchId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "IRR";
    public DateOnly ReceivedOn { get; set; }
    public string? Reference { get; set; }
    public string? ChequeNumber { get; set; }
    public string? ChequeBank { get; set; }
    public DateOnly? ChequeDueDate { get; set; }
    public ChequeStatus? ChequeStatus { get; set; }
    public string? Provider { get; set; }
    public string? ProviderAuthority { get; set; }
    public string? ProviderRef { get; set; }
    public Guid? TargetInvoiceId { get; set; }
    public Guid? TargetInstallmentId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ReceiptNumber { get; set; }
    public decimal Allocated { get; set; }
    public decimal Refunded { get; set; }
    public string? FailureCode { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? SucceededAt { get; set; }
    public List<PaymentAllocation> Allocations { get; set; } = [];

    /// <summary>Credit balance carried by this payment (succeeded only).</summary>
    public decimal Unallocated => Status == PaymentStatus.Succeeded ? Amount - Allocated - Refunded : 0;
}

public sealed class PaymentAllocation : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid? InstallmentId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Refund : TenantEntity
{
    public Guid PaymentId { get; set; }
    public Guid StudentId { get; set; }
    public Guid BranchId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "IRR";
    public string Reason { get; set; } = string.Empty;
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public Guid? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    /// <summary>The EEMP-011 workflow instance deciding this request (DL-081); null for requests decided by the local M3 maker-checker.</summary>
    public Guid? WorkflowInstanceId { get; set; }
    public bool IsOpen => Status is ApprovalStatus.Pending or ApprovalStatus.Returned;
}

/// <summary>Per-tenant gap-free counter (DL-060). Incremented in the same transaction as the numbered document.</summary>
public sealed class NumberSeries : ITenantOwned, IHasRowVersion
{
    public Guid TenantId { get; set; }
    public string Series { get; set; } = string.Empty;
    public long LastValue { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Append-only finance audit (DL-063). Written in the same SaveChanges as the change; never updated or deleted.</summary>
public sealed class FinanceAuditEntry : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTime At { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? StudentId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Details { get; set; }
}
