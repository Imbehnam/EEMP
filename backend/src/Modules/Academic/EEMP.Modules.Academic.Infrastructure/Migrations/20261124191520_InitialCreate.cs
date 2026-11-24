using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMP.Modules.Academic.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "academic");

            migrationBuilder.CreateTable(
                name: "AcademicYears",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademicYears", x => x.Id);
                    table.UniqueConstraint("AK_AcademicYears_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "GradeAuditEntries",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OldScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    NewScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    OldMark = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NewMark = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GradingSchemes",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MinScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    MaxScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    PassScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    MissingCountsAsZero = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingSchemes", x => x.Id);
                    table.UniqueConstraint("AK_GradingSchemes_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                    table.UniqueConstraint("AK_Subjects_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Terms",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terms", x => x.Id);
                    table.UniqueConstraint("AK_Terms_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Terms_AcademicYears_TenantId_AcademicYearId",
                        columns: x => new { x.TenantId, x.AcademicYearId },
                        principalSchema: "academic",
                        principalTable: "AcademicYears",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradeBands",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GradingSchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MinValue = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeBands_GradingSchemes_TenantId_GradingSchemeId",
                        columns: x => new { x.TenantId, x.GradingSchemeId },
                        principalSchema: "academic",
                        principalTable: "GradingSchemes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Courses",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GradeLevel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GradingSchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                    table.UniqueConstraint("AK_Courses_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Courses_GradingSchemes_TenantId_GradingSchemeId",
                        columns: x => new { x.TenantId, x.GradingSchemeId },
                        principalSchema: "academic",
                        principalTable: "GradingSchemes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Courses_Subjects_TenantId_SubjectId",
                        columns: x => new { x.TenantId, x.SubjectId },
                        principalSchema: "academic",
                        principalTable: "Subjects",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportCards",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
                    PublishedVersion = table.Column<int>(type: "int", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AverageValue = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportCards", x => x.Id);
                    table.UniqueConstraint("AK_ReportCards_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReportCards_Terms_TenantId_TermId",
                        columns: x => new { x.TenantId, x.TermId },
                        principalSchema: "academic",
                        principalTable: "Terms",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Classes",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    EnrolledCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GradingSchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefaultRoom = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classes", x => x.Id);
                    table.UniqueConstraint("AK_Classes_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Classes_Courses_TenantId_CourseId",
                        columns: x => new { x.TenantId, x.CourseId },
                        principalSchema: "academic",
                        principalTable: "Courses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Classes_GradingSchemes_TenantId_GradingSchemeId",
                        columns: x => new { x.TenantId, x.GradingSchemeId },
                        principalSchema: "academic",
                        principalTable: "GradingSchemes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Classes_Terms_TenantId_TermId",
                        columns: x => new { x.TenantId, x.TermId },
                        principalSchema: "academic",
                        principalTable: "Terms",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportCardVersions",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportCardVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportCardVersions_ReportCards_TenantId_ReportCardId",
                        columns: x => new { x.TenantId, x.ReportCardId },
                        principalSchema: "academic",
                        principalTable: "ReportCards",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assessments",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    MaxScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PublishedWeight = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    PublishedMaxScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assessments", x => x.Id);
                    table.UniqueConstraint("AK_Assessments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Assessments_Classes_TenantId_ClassId",
                        columns: x => new { x.TenantId, x.ClassId },
                        principalSchema: "academic",
                        principalTable: "Classes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceSessions",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduleSlotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TakenByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSessions", x => x.Id);
                    table.UniqueConstraint("AK_AttendanceSessions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceSessions_Classes_TenantId_ClassId",
                        columns: x => new { x.TenantId, x.ClassId },
                        principalSchema: "academic",
                        principalTable: "Classes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Enrollments",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EnrolledOn = table.Column<DateOnly>(type: "date", nullable: false),
                    WithdrawnOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enrollments", x => x.Id);
                    table.UniqueConstraint("AK_Enrollments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Enrollments_Classes_TenantId_ClassId",
                        columns: x => new { x.TenantId, x.ClassId },
                        principalSchema: "academic",
                        principalTable: "Classes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleSlots",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Weekday = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    Room = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSlots", x => x.Id);
                    table.UniqueConstraint("AK_ScheduleSlots_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_Classes_TenantId_ClassId",
                        columns: x => new { x.TenantId, x.ClassId },
                        principalSchema: "academic",
                        principalTable: "Classes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherAssignments",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherAssignments_Classes_TenantId_ClassId",
                        columns: x => new { x.TenantId, x.ClassId },
                        principalSchema: "academic",
                        principalTable: "Classes",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentResults",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    Mark = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    PublishedScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    PublishedMark = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PublishedComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentResults_Assessments_TenantId_AssessmentId",
                        columns: x => new { x.TenantId, x.AssessmentId },
                        principalSchema: "academic",
                        principalTable: "Assessments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MarkedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MarkedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_AttendanceSessions_TenantId_SessionId",
                        columns: x => new { x.TenantId, x.SessionId },
                        principalSchema: "academic",
                        principalTable: "AttendanceSessions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademicYears_TenantId",
                schema: "academic",
                table: "AcademicYears",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AcademicYears_TenantId_Name",
                schema: "academic",
                table: "AcademicYears",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentResults_TenantId",
                schema: "academic",
                table: "AssessmentResults",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentResults_TenantId_AssessmentId_StudentId",
                schema: "academic",
                table: "AssessmentResults",
                columns: new[] { "TenantId", "AssessmentId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentResults_TenantId_StudentId",
                schema: "academic",
                table: "AssessmentResults",
                columns: new[] { "TenantId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_TenantId",
                schema: "academic",
                table: "Assessments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_TenantId_ClassId",
                schema: "academic",
                table: "Assessments",
                columns: new[] { "TenantId", "ClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_TenantId",
                schema: "academic",
                table: "AttendanceRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_TenantId_SessionId_StudentId",
                schema: "academic",
                table: "AttendanceRecords",
                columns: new[] { "TenantId", "SessionId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_TenantId_StudentId",
                schema: "academic",
                table: "AttendanceRecords",
                columns: new[] { "TenantId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_TenantId",
                schema: "academic",
                table: "AttendanceSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_TenantId_ClassId_Date_ScheduleSlotId",
                schema: "academic",
                table: "AttendanceSessions",
                columns: new[] { "TenantId", "ClassId", "Date", "ScheduleSlotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Classes_TenantId",
                schema: "academic",
                table: "Classes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_TenantId_BranchId_TermId",
                schema: "academic",
                table: "Classes",
                columns: new[] { "TenantId", "BranchId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_Classes_TenantId_CourseId",
                schema: "academic",
                table: "Classes",
                columns: new[] { "TenantId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Classes_TenantId_GradingSchemeId",
                schema: "academic",
                table: "Classes",
                columns: new[] { "TenantId", "GradingSchemeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Classes_TenantId_TermId_Code",
                schema: "academic",
                table: "Classes",
                columns: new[] { "TenantId", "TermId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_TenantId",
                schema: "academic",
                table: "Courses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_TenantId_Code",
                schema: "academic",
                table: "Courses",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_TenantId_GradingSchemeId",
                schema: "academic",
                table: "Courses",
                columns: new[] { "TenantId", "GradingSchemeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_TenantId_SubjectId",
                schema: "academic",
                table: "Courses",
                columns: new[] { "TenantId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_TenantId",
                schema: "academic",
                table: "Enrollments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_TenantId_ClassId_StudentId",
                schema: "academic",
                table: "Enrollments",
                columns: new[] { "TenantId", "ClassId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_TenantId_StudentId",
                schema: "academic",
                table: "Enrollments",
                columns: new[] { "TenantId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeAuditEntries_TenantId",
                schema: "academic",
                table: "GradeAuditEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeAuditEntries_TenantId_AssessmentId_OccurredAt",
                schema: "academic",
                table: "GradeAuditEntries",
                columns: new[] { "TenantId", "AssessmentId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeBands_TenantId",
                schema: "academic",
                table: "GradeBands",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeBands_TenantId_GradingSchemeId",
                schema: "academic",
                table: "GradeBands",
                columns: new[] { "TenantId", "GradingSchemeId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradingSchemes_TenantId",
                schema: "academic",
                table: "GradingSchemes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GradingSchemes_TenantId_Code",
                schema: "academic",
                table: "GradingSchemes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportCards_TenantId",
                schema: "academic",
                table: "ReportCards",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportCards_TenantId_StudentId_TermId",
                schema: "academic",
                table: "ReportCards",
                columns: new[] { "TenantId", "StudentId", "TermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportCards_TenantId_TermId_BranchId",
                schema: "academic",
                table: "ReportCards",
                columns: new[] { "TenantId", "TermId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportCardVersions_TenantId",
                schema: "academic",
                table: "ReportCardVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportCardVersions_TenantId_ReportCardId_VersionNo",
                schema: "academic",
                table: "ReportCardVersions",
                columns: new[] { "TenantId", "ReportCardId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_TenantId",
                schema: "academic",
                table: "ScheduleSlots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_TenantId_ClassId",
                schema: "academic",
                table: "ScheduleSlots",
                columns: new[] { "TenantId", "ClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_TenantId",
                schema: "academic",
                table: "Subjects",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_TenantId_Code",
                schema: "academic",
                table: "Subjects",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAssignments_TenantId",
                schema: "academic",
                table: "TeacherAssignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAssignments_TenantId_ClassId_TeacherId",
                schema: "academic",
                table: "TeacherAssignments",
                columns: new[] { "TenantId", "ClassId", "TeacherId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAssignments_TenantId_TeacherId",
                schema: "academic",
                table: "TeacherAssignments",
                columns: new[] { "TenantId", "TeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_Terms_TenantId",
                schema: "academic",
                table: "Terms",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Terms_TenantId_AcademicYearId_Name",
                schema: "academic",
                table: "Terms",
                columns: new[] { "TenantId", "AcademicYearId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentResults",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "AttendanceRecords",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "Enrollments",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "GradeAuditEntries",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "GradeBands",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "ReportCardVersions",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "ScheduleSlots",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "TeacherAssignments",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "Assessments",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "AttendanceSessions",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "ReportCards",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "Classes",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "Courses",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "Terms",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "GradingSchemes",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "Subjects",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "AcademicYears",
                schema: "academic");
        }
    }
}
