using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMP.Modules.Consultation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "consultation");

            migrationBuilder.CreateTable(
                name: "Consultants",
                schema: "consultation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Bio = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Specialties = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consultants", x => x.Id);
                    table.UniqueConstraint("AK_Consultants_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "consultation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancellationWindowHours = table.Column<int>(type: "int", nullable: false),
                    BookingHorizonDays = table.Column<int>(type: "int", nullable: false),
                    MinLeadHours = table.Column<int>(type: "int", nullable: false),
                    MaxActiveBookingsPerStudent = table.Column<int>(type: "int", nullable: false),
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
                name: "Tickets",
                schema: "consultation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                    table.UniqueConstraint("AK_Tickets_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ConsultantBranches",
                schema: "consultation",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultantBranches", x => new { x.TenantId, x.ConsultantId, x.BranchId });
                    table.ForeignKey(
                        name: "FK_ConsultantBranches_Consultants_TenantId_ConsultantId",
                        columns: x => new { x.TenantId, x.ConsultantId },
                        principalSchema: "consultation",
                        principalTable: "Consultants",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Slots",
                schema: "consultation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Slots", x => x.Id);
                    table.UniqueConstraint("AK_Slots_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Slots_Consultants_TenantId_ConsultantId",
                        columns: x => new { x.TenantId, x.ConsultantId },
                        principalSchema: "consultation",
                        principalTable: "Consultants",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TicketMessages",
                schema: "consultation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStaff = table.Column<bool>(type: "bit", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketMessages_Tickets_TenantId_TicketId",
                        columns: x => new { x.TenantId, x.TicketId },
                        principalSchema: "consultation",
                        principalTable: "Tickets",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                schema: "consultation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Topic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequesterNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StatusChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.UniqueConstraint("AK_Bookings_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Bookings_Consultants_TenantId_ConsultantId",
                        columns: x => new { x.TenantId, x.ConsultantId },
                        principalSchema: "consultation",
                        principalTable: "Consultants",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Slots_TenantId_SlotId",
                        columns: x => new { x.TenantId, x.SlotId },
                        principalSchema: "consultation",
                        principalTable: "Slots",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingNotes",
                schema: "consultation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingNotes_Bookings_TenantId_BookingId",
                        columns: x => new { x.TenantId, x.BookingId },
                        principalSchema: "consultation",
                        principalTable: "Bookings",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingNotes_TenantId",
                schema: "consultation",
                table: "BookingNotes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingNotes_TenantId_BookingId_CreatedAt",
                schema: "consultation",
                table: "BookingNotes",
                columns: new[] { "TenantId", "BookingId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TenantId",
                schema: "consultation",
                table: "Bookings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TenantId_BranchId_StartsAt",
                schema: "consultation",
                table: "Bookings",
                columns: new[] { "TenantId", "BranchId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TenantId_ConsultantId_StartsAt",
                schema: "consultation",
                table: "Bookings",
                columns: new[] { "TenantId", "ConsultantId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TenantId_StudentId_Status",
                schema: "consultation",
                table: "Bookings",
                columns: new[] { "TenantId", "StudentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Bookings_ActiveSlot",
                schema: "consultation",
                table: "Bookings",
                columns: new[] { "TenantId", "SlotId" },
                unique: true,
                filter: "[Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultantBranches_TenantId",
                schema: "consultation",
                table: "ConsultantBranches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultantBranches_TenantId_BranchId",
                schema: "consultation",
                table: "ConsultantBranches",
                columns: new[] { "TenantId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Consultants_TenantId",
                schema: "consultation",
                table: "Consultants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Consultants_TenantId_UserId",
                schema: "consultation",
                table: "Consultants",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Settings_TenantId",
                schema: "consultation",
                table: "Settings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Slots_TenantId",
                schema: "consultation",
                table: "Slots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Slots_TenantId_BranchId_StartsAt",
                schema: "consultation",
                table: "Slots",
                columns: new[] { "TenantId", "BranchId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Slots_TenantId_ConsultantId_StartsAt",
                schema: "consultation",
                table: "Slots",
                columns: new[] { "TenantId", "ConsultantId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TenantId",
                schema: "consultation",
                table: "TicketMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TenantId_TicketId_CreatedAt",
                schema: "consultation",
                table: "TicketMessages",
                columns: new[] { "TenantId", "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TenantId",
                schema: "consultation",
                table: "Tickets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TenantId_BranchId_Status_LastMessageAt",
                schema: "consultation",
                table: "Tickets",
                columns: new[] { "TenantId", "BranchId", "Status", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TenantId_CreatedByUserId",
                schema: "consultation",
                table: "Tickets",
                columns: new[] { "TenantId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TenantId_Number",
                schema: "consultation",
                table: "Tickets",
                columns: new[] { "TenantId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingNotes",
                schema: "consultation");

            migrationBuilder.DropTable(
                name: "ConsultantBranches",
                schema: "consultation");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "consultation");

            migrationBuilder.DropTable(
                name: "TicketMessages",
                schema: "consultation");

            migrationBuilder.DropTable(
                name: "Bookings",
                schema: "consultation");

            migrationBuilder.DropTable(
                name: "Tickets",
                schema: "consultation");

            migrationBuilder.DropTable(
                name: "Slots",
                schema: "consultation");

            migrationBuilder.DropTable(
                name: "Consultants",
                schema: "consultation");
        }
    }
}
