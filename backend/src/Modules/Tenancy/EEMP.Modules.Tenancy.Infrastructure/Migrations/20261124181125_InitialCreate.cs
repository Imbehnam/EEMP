using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMP.Modules.Tenancy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefaultLocale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SupportedLocales = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                schema: "tenancy",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "tenancy");
        }
    }
}
