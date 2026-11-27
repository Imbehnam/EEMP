using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMP.Modules.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                schema: "finance",
                table: "Refunds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                schema: "finance",
                table: "InvoiceDiscounts",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                schema: "finance",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                schema: "finance",
                table: "InvoiceDiscounts");
        }
    }
}
