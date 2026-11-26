using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMP.Modules.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "FeeTypes",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Taxable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeeTypes", x => x.Id);
                    table.UniqueConstraint("AK_FeeTypes_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "FinanceAudit",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    LateFeeTotal = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreditTotal = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.UniqueConstraint("AK_Invoices_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "NumberSeries",
                schema: "finance",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Series = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSeries", x => new { x.TenantId, x.Series });
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChequeNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ChequeBank = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChequeDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ChequeStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProviderAuthority = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TargetInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetInstallmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Allocated = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Refunded = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SucceededAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.UniqueConstraint("AK_Payments_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VatEnabled = table.Column<bool>(type: "bit", nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    LateFeeEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LateFeeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LateFeeValue = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    LateFeeGraceDays = table.Column<int>(type: "int", nullable: false),
                    SiblingDiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    BlockResultsOnOverdue = table.Column<bool>(type: "bit", nullable: false),
                    OnlinePaymentsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.UniqueConstraint("AK_Settings_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "FeeItems",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeeItems", x => x.Id);
                    table.UniqueConstraint("AK_FeeItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_FeeItems_FeeTypes_TenantId_FeeTypeId",
                        columns: x => new { x.TenantId, x.FeeTypeId },
                        principalSchema: "finance",
                        principalTable: "FeeTypes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Installments",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeqNo = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    LateFeeCharged = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installments_Invoices_TenantId_InvoiceId",
                        columns: x => new { x.TenantId, x.InvoiceId },
                        principalSchema: "finance",
                        principalTable: "Invoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceAdjustments",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    InstallmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceAdjustments_Invoices_TenantId_InvoiceId",
                        columns: x => new { x.TenantId, x.InvoiceId },
                        principalSchema: "finance",
                        principalTable: "Invoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceDiscounts",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceDiscounts", x => x.Id);
                    table.UniqueConstraint("AK_InvoiceDiscounts_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_InvoiceDiscounts_Invoices_TenantId_InvoiceId",
                        columns: x => new { x.TenantId, x.InvoiceId },
                        principalSchema: "finance",
                        principalTable: "Invoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeeItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EnrollmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Taxable = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_TenantId_InvoiceId",
                        columns: x => new { x.TenantId, x.InvoiceId },
                        principalSchema: "finance",
                        principalTable: "Invoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAllocations",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Invoices_TenantId_InvoiceId",
                        columns: x => new { x.TenantId, x.InvoiceId },
                        principalSchema: "finance",
                        principalTable: "Invoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Payments_TenantId_PaymentId",
                        columns: x => new { x.TenantId, x.PaymentId },
                        principalSchema: "finance",
                        principalTable: "Payments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.UniqueConstraint("AK_Refunds_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Refunds_Payments_TenantId_PaymentId",
                        columns: x => new { x.TenantId, x.PaymentId },
                        principalSchema: "finance",
                        principalTable: "Payments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedCharges",
                schema: "finance",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeeItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedCharges", x => new { x.TenantId, x.EnrollmentId, x.FeeItemId });
                    table.ForeignKey(
                        name: "FK_GeneratedCharges_FeeItems_TenantId_FeeItemId",
                        columns: x => new { x.TenantId, x.FeeItemId },
                        principalSchema: "finance",
                        principalTable: "FeeItems",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneratedCharges_Invoices_TenantId_InvoiceId",
                        columns: x => new { x.TenantId, x.InvoiceId },
                        principalSchema: "finance",
                        principalTable: "Invoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeeItems_TenantId",
                schema: "finance",
                table: "FeeItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FeeItems_TenantId_FeeTypeId",
                schema: "finance",
                table: "FeeItems",
                columns: new[] { "TenantId", "FeeTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeeItems_TenantId_TermId",
                schema: "finance",
                table: "FeeItems",
                columns: new[] { "TenantId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeeTypes_TenantId",
                schema: "finance",
                table: "FeeTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FeeTypes_TenantId_Code",
                schema: "finance",
                table: "FeeTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAudit_TenantId",
                schema: "finance",
                table: "FinanceAudit",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAudit_TenantId_At",
                schema: "finance",
                table: "FinanceAudit",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAudit_TenantId_InvoiceId",
                schema: "finance",
                table: "FinanceAudit",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAudit_TenantId_PaymentId",
                schema: "finance",
                table: "FinanceAudit",
                columns: new[] { "TenantId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedCharges_TenantId",
                schema: "finance",
                table: "GeneratedCharges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedCharges_TenantId_FeeItemId",
                schema: "finance",
                table: "GeneratedCharges",
                columns: new[] { "TenantId", "FeeItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedCharges_TenantId_InvoiceId",
                schema: "finance",
                table: "GeneratedCharges",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Installments_TenantId",
                schema: "finance",
                table: "Installments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Installments_TenantId_InvoiceId_SeqNo",
                schema: "finance",
                table: "Installments",
                columns: new[] { "TenantId", "InvoiceId", "SeqNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAdjustments_TenantId",
                schema: "finance",
                table: "InvoiceAdjustments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAdjustments_TenantId_InvoiceId",
                schema: "finance",
                table: "InvoiceAdjustments",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAdjustments_TenantId_Number",
                schema: "finance",
                table: "InvoiceAdjustments",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDiscounts_TenantId",
                schema: "finance",
                table: "InvoiceDiscounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDiscounts_TenantId_InvoiceId",
                schema: "finance",
                table: "InvoiceDiscounts",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDiscounts_TenantId_Status",
                schema: "finance",
                table: "InvoiceDiscounts",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_TenantId",
                schema: "finance",
                table: "InvoiceLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_TenantId_InvoiceId",
                schema: "finance",
                table: "InvoiceLines",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId",
                schema: "finance",
                table: "Invoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_BranchId_Status",
                schema: "finance",
                table: "Invoices",
                columns: new[] { "TenantId", "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_Number",
                schema: "finance",
                table: "Invoices",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_StudentId_Status",
                schema: "finance",
                table: "Invoices",
                columns: new[] { "TenantId", "StudentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NumberSeries_TenantId",
                schema: "finance",
                table: "NumberSeries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId",
                schema: "finance",
                table: "PaymentAllocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId_InvoiceId",
                schema: "finance",
                table: "PaymentAllocations",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId_PaymentId",
                schema: "finance",
                table: "PaymentAllocations",
                columns: new[] { "TenantId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId",
                schema: "finance",
                table: "Payments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_BranchId_ReceivedOn",
                schema: "finance",
                table: "Payments",
                columns: new[] { "TenantId", "BranchId", "ReceivedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_IdempotencyKey",
                schema: "finance",
                table: "Payments",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_Provider_ProviderAuthority",
                schema: "finance",
                table: "Payments",
                columns: new[] { "TenantId", "Provider", "ProviderAuthority" },
                unique: true,
                filter: "[ProviderAuthority] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_ReceiptNumber",
                schema: "finance",
                table: "Payments",
                columns: new[] { "TenantId", "ReceiptNumber" },
                unique: true,
                filter: "[ReceiptNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_StudentId",
                schema: "finance",
                table: "Payments",
                columns: new[] { "TenantId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId",
                schema: "finance",
                table: "Refunds",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId_PaymentId",
                schema: "finance",
                table: "Refunds",
                columns: new[] { "TenantId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId_Status",
                schema: "finance",
                table: "Refunds",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Settings_Tenant",
                schema: "finance",
                table: "Settings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceAudit",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "GeneratedCharges",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Installments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "InvoiceAdjustments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "InvoiceDiscounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "InvoiceLines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "NumberSeries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PaymentAllocations",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Refunds",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "FeeItems",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Payments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "FeeTypes",
                schema: "finance");
        }
    }
}
