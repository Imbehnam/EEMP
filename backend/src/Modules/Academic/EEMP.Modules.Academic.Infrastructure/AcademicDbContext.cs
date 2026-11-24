using EEMP.Infrastructure.Persistence;
using EEMP.Modules.Academic.Application;
using EEMP.Modules.Academic.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMP.Modules.Academic.Infrastructure;

/// <summary>
/// Academic module persistence (schema "academic"): catalog, classes, enrollment, attendance, assessment and report cards.
/// Every table is tenant-owned with a tenant-leading index; intra-module foreign keys are tenant-composite (TenantId, Id)
/// so rows can never reference another tenant's rows. Only pure children cascade (avoids SQL Server multiple cascade paths).
/// </summary>
public sealed class AcademicDbContext(DbContextOptions<AcademicDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IAcademicDbContext
{
    public const string SchemaName = "academic";
    protected override string Schema => SchemaName;

    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<GradingScheme> GradingSchemes => Set<GradingScheme>();
    public DbSet<GradeBand> GradeBands => Set<GradeBand>();
    public DbSet<ClassSection> Classes => Set<ClassSection>();
    public DbSet<ScheduleSlot> ScheduleSlots => Set<ScheduleSlot>();
    public DbSet<TeacherAssignment> TeacherAssignments => Set<TeacherAssignment>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentResult> AssessmentResults => Set<AssessmentResult>();
    public DbSet<GradeAuditEntry> GradeAudit => Set<GradeAuditEntry>();
    public DbSet<ReportCard> ReportCards => Set<ReportCard>();
    public DbSet<ReportCardVersion> ReportCardVersions => Set<ReportCardVersion>();

    public void ResetTracking() => ChangeTracker.Clear();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AcademicYear>(b =>
        {
            Root(b, "AcademicYears");
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });
        modelBuilder.Entity<Term>(b =>
        {
            Root(b, "Terms");
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.AcademicYearId, x.Name }).IsUnique();
            Ref<Term, AcademicYear>(b, x => x.AcademicYearId);
        });
        modelBuilder.Entity<Subject>(b =>
        {
            Root(b, "Subjects");
            b.Property(x => x.Code).HasMaxLength(30).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(1000);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        modelBuilder.Entity<GradingScheme>(b =>
        {
            Root(b, "GradingSchemes");
            b.Property(x => x.Code).HasMaxLength(30).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.MinScore).HasPrecision(9, 2);
            b.Property(x => x.MaxScore).HasPrecision(9, 2);
            b.Property(x => x.PassScore).HasPrecision(9, 2);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasMany(x => x.Bands).WithOne().HasForeignKey(x => new { x.TenantId, x.GradingSchemeId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<GradeBand>(b =>
        {
            b.ToTable("GradeBands");
            b.HasKey(x => x.Id);
            b.Property(x => x.Label).HasMaxLength(30).IsRequired();
            b.Property(x => x.MinValue).HasPrecision(9, 2);
            b.HasIndex(x => new { x.TenantId, x.GradingSchemeId });
        });
        modelBuilder.Entity<Course>(b =>
        {
            Root(b, "Courses");
            b.Property(x => x.Code).HasMaxLength(30).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.GradeLevel).HasMaxLength(50);
            b.Property(x => x.Description).HasMaxLength(1000);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            Ref<Course, Subject>(b, x => x.SubjectId);
            b.HasOne<GradingScheme>().WithMany().HasForeignKey(x => new { x.TenantId, x.GradingSchemeId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ClassSection>(b =>
        {
            Root(b, "Classes");
            b.Property(x => x.Code).HasMaxLength(30).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.DefaultRoom).HasMaxLength(50);
            b.HasIndex(x => new { x.TenantId, x.TermId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.BranchId, x.TermId });
            Ref<ClassSection, Term>(b, x => x.TermId);
            Ref<ClassSection, Course>(b, x => x.CourseId);
            Ref<ClassSection, GradingScheme>(b, x => x.GradingSchemeId);
            b.HasMany(x => x.Schedule).WithOne().HasForeignKey(x => new { x.TenantId, x.ClassId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Teachers).WithOne().HasForeignKey(x => new { x.TenantId, x.ClassId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ScheduleSlot>(b =>
        {
            b.ToTable("ScheduleSlots");
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.Property(x => x.Weekday).HasConversion<string>().HasMaxLength(10);
            b.Property(x => x.Room).HasMaxLength(50);
            b.HasIndex(x => new { x.TenantId, x.ClassId });
        });
        modelBuilder.Entity<TeacherAssignment>(b =>
        {
            b.ToTable("TeacherAssignments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.TenantId, x.ClassId, x.TeacherId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.TeacherId });
        });
        modelBuilder.Entity<Enrollment>(b =>
        {
            Root(b, "Enrollments");
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.TenantId, x.ClassId, x.StudentId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.StudentId });
            Ref<Enrollment, ClassSection>(b, x => x.ClassId);
        });
        modelBuilder.Entity<AttendanceSession>(b =>
        {
            Root(b, "AttendanceSessions");
            // One sheet per class, date and slot: the key of the idempotent bulk upsert.
            b.HasIndex(x => new { x.TenantId, x.ClassId, x.Date, x.ScheduleSlotId }).IsUnique();
            Ref<AttendanceSession, ClassSection>(b, x => x.ClassId);
            b.HasMany(x => x.Records).WithOne().HasForeignKey(x => new { x.TenantId, x.SessionId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AttendanceRecord>(b =>
        {
            b.ToTable("AttendanceRecords");
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.SessionId, x.StudentId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.StudentId });
        });
        modelBuilder.Entity<Assessment>(b =>
        {
            Root(b, "Assessments");
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Weight).HasPrecision(9, 2);
            b.Property(x => x.MaxScore).HasPrecision(9, 2);
            b.Property(x => x.PublishedWeight).HasPrecision(9, 2);
            b.Property(x => x.PublishedMaxScore).HasPrecision(9, 2);
            b.Property(x => x.ReturnReason).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.ClassId });
            Ref<Assessment, ClassSection>(b, x => x.ClassId);
            b.HasMany(x => x.Results).WithOne().HasForeignKey(x => new { x.TenantId, x.AssessmentId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AssessmentResult>(b =>
        {
            b.ToTable("AssessmentResults");
            b.HasKey(x => x.Id);
            b.Property(x => x.Score).HasPrecision(9, 2);
            b.Property(x => x.PublishedScore).HasPrecision(9, 2);
            b.Property(x => x.Mark).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.PublishedMark).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Comment).HasMaxLength(500);
            b.Property(x => x.PublishedComment).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.AssessmentId, x.StudentId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.StudentId });
        });
        modelBuilder.Entity<GradeAuditEntry>(b =>
        {
            b.ToTable("GradeAuditEntries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.OldScore).HasPrecision(9, 2);
            b.Property(x => x.NewScore).HasPrecision(9, 2);
            b.Property(x => x.OldMark).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.NewMark).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Reason).HasMaxLength(500);
            // Append-only history; deliberately no FK so it outlives a deleted draft assessment.
            b.HasIndex(x => new { x.TenantId, x.AssessmentId, x.OccurredAt });
        });
        modelBuilder.Entity<ReportCard>(b =>
        {
            Root(b, "ReportCards");
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.AverageValue).HasPrecision(9, 2);
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.TermId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.TermId, x.BranchId });
            Ref<ReportCard, Term>(b, x => x.TermId);
            b.HasMany(x => x.Versions).WithOne().HasForeignKey(x => new { x.TenantId, x.ReportCardId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ReportCardVersion>(b =>
        {
            b.ToTable("ReportCardVersions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Content).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.ReportCardId, x.VersionNo }).IsUnique();
        });

        // Ids are assigned in code (Guid.NewGuid()); children added through navigations must be tracked as Added, not Modified.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            entity.FindProperty("Id")?.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }

    private static void Root<T>(EntityTypeBuilder<T> b, string table) where T : EEMP.SharedKernel.Domain.TenantEntity
    {
        b.ToTable(table);
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
    }

    /// <summary>Tenant-composite, non-cascading reference to another academic aggregate.</summary>
    private static void Ref<T, TPrincipal>(EntityTypeBuilder<T> b, System.Linq.Expressions.Expression<Func<T, Guid>> fk)
        where T : EEMP.SharedKernel.Domain.TenantEntity where TPrincipal : EEMP.SharedKernel.Domain.TenantEntity
    {
        var name = ((System.Linq.Expressions.MemberExpression)fk.Body).Member.Name;
        b.HasOne<TPrincipal>().WithMany().HasForeignKey(nameof(EEMP.SharedKernel.Domain.TenantEntity.TenantId), name)
            .HasPrincipalKey(nameof(EEMP.SharedKernel.Domain.TenantEntity.TenantId), nameof(EEMP.SharedKernel.Domain.TenantEntity.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Used by dotnet-ef only (see EEMP.Infrastructure.Persistence.DesignTime).</summary>
internal sealed class AcademicDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AcademicDbContext>
{
    public AcademicDbContext CreateDbContext(string[] args) => new(DesignTime.Options<AcademicDbContext>(AcademicDbContext.SchemaName), DesignTime.NoTenant());
}
