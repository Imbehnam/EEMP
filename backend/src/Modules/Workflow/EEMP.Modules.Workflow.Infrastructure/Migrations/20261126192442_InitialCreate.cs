using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMP.Modules.Workflow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "Definitions",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubjectModule = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PublishedVersion = table.Column<int>(type: "int", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Definitions", x => x.Id);
                    table.UniqueConstraint("AK_Definitions_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "DefinitionVersions",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequireDistinctApprovers = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefinitionVersions", x => x.Id);
                    table.UniqueConstraint("AK_DefinitionVersions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_DefinitionVersions_Definitions_TenantId_DefinitionId",
                        columns: x => new { x.TenantId, x.DefinitionId },
                        principalSchema: "workflow",
                        principalTable: "Definitions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DefinitionSteps",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SlaHours = table.Column<int>(type: "int", nullable: true),
                    AllowReturn = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefinitionSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefinitionSteps_DefinitionVersions_TenantId_VersionId",
                        columns: x => new { x.TenantId, x.VersionId },
                        principalSchema: "workflow",
                        principalTable: "DefinitionVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Instances",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    DefinitionKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubjectModule = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequireDistinctApprovers = table.Column<bool>(type: "bit", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    StepCount = table.Column<int>(type: "int", nullable: false),
                    CurrentStepOrder = table.Column<int>(type: "int", nullable: true),
                    CurrentStepName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CurrentStepPermission = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CurrentStepAssigneeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentStepEnteredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentStepDueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instances", x => x.Id);
                    table.UniqueConstraint("AK_Instances_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Instances_DefinitionVersions_TenantId_VersionId",
                        columns: x => new { x.TenantId, x.VersionId },
                        principalSchema: "workflow",
                        principalTable: "DefinitionVersions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Instances_Definitions_TenantId_DefinitionId",
                        columns: x => new { x.TenantId, x.DefinitionId },
                        principalSchema: "workflow",
                        principalTable: "Definitions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstanceHistory",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: true),
                    StepName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstanceHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstanceHistory_Instances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalSchema: "workflow",
                        principalTable: "Instances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectModule = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.UniqueConstraint("AK_OutboxMessages_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_OutboxMessages_Instances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalSchema: "workflow",
                        principalTable: "Instances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Definitions_TenantId",
                schema: "workflow",
                table: "Definitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Definitions_TenantId_Key",
                schema: "workflow",
                table: "Definitions",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionSteps_TenantId",
                schema: "workflow",
                table: "DefinitionSteps",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionSteps_TenantId_VersionId_Order",
                schema: "workflow",
                table: "DefinitionSteps",
                columns: new[] { "TenantId", "VersionId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionVersions_TenantId",
                schema: "workflow",
                table: "DefinitionVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionVersions_TenantId_DefinitionId_VersionNo",
                schema: "workflow",
                table: "DefinitionVersions",
                columns: new[] { "TenantId", "DefinitionId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstanceHistory_TenantId",
                schema: "workflow",
                table: "InstanceHistory",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InstanceHistory_TenantId_InstanceId_RequestId",
                schema: "workflow",
                table: "InstanceHistory",
                columns: new[] { "TenantId", "InstanceId", "RequestId" },
                unique: true,
                filter: "[RequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InstanceHistory_TenantId_InstanceId_Sequence",
                schema: "workflow",
                table: "InstanceHistory",
                columns: new[] { "TenantId", "InstanceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instances_TenantId",
                schema: "workflow",
                table: "Instances",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_TenantId_DefinitionId",
                schema: "workflow",
                table: "Instances",
                columns: new[] { "TenantId", "DefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_TenantId_RequestedByUserId",
                schema: "workflow",
                table: "Instances",
                columns: new[] { "TenantId", "RequestedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_TenantId_Status_CurrentStepPermission_BranchId",
                schema: "workflow",
                table: "Instances",
                columns: new[] { "TenantId", "Status", "CurrentStepPermission", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_TenantId_SubjectModule_IdempotencyKey",
                schema: "workflow",
                table: "Instances",
                columns: new[] { "TenantId", "SubjectModule", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instances_TenantId_SubjectModule_SubjectType_SubjectId",
                schema: "workflow",
                table: "Instances",
                columns: new[] { "TenantId", "SubjectModule", "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_TenantId_VersionId",
                schema: "workflow",
                table: "Instances",
                columns: new[] { "TenantId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_NextAttemptAt",
                schema: "workflow",
                table: "OutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_TenantId",
                schema: "workflow",
                table: "OutboxMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_TenantId_InstanceId",
                schema: "workflow",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "InstanceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DefinitionSteps",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "InstanceHistory",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "Instances",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "DefinitionVersions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "Definitions",
                schema: "workflow");
        }
    }
}
