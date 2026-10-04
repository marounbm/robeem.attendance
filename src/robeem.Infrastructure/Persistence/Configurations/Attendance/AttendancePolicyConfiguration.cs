using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendancePolicyConfiguration : IEntityTypeConfiguration<AttendancePolicy>
{
    public void Configure(EntityTypeBuilder<AttendancePolicy> builder)
    {
        builder.Property(p => p.CorrectionApprovalWorkflow).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.UnlabeledPunchInterpretationMode).HasConversion<string>().HasMaxLength(32);

        // At most one policy per schedule, and one tenant-wide default (TimeScheduleId null).
        // A plain unique index would not do: NULLs are distinct in it, so it would allow any number
        // of defaults. The filters are quoted identifiers, valid on both PostgreSQL and SQLite.
        builder.HasIndex(p => new { p.TenantId, p.TimeScheduleId })
            .IsUnique()
            .HasFilter($"\"{nameof(AttendancePolicy.TimeScheduleId)}\" IS NOT NULL");
        builder.HasIndex(p => p.TenantId)
            .IsUnique()
            .HasFilter($"\"{nameof(AttendancePolicy.TimeScheduleId)}\" IS NULL");
    }
}
