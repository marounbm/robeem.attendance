using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceJobRunConfiguration : IEntityTypeConfiguration<AttendanceJobRun>
{
    public void Configure(EntityTypeBuilder<AttendanceJobRun> builder)
    {
        builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);

        // The history of one job, newest first; and pruning by age.
        builder.HasIndex(r => new { r.TenantId, r.JobName, r.Id });
        builder.HasIndex(r => new { r.TenantId, r.StartedAt });
    }
}
