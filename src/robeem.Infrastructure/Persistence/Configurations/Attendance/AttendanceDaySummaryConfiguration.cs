using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceDaySummaryConfiguration : IEntityTypeConfiguration<AttendanceDaySummary>
{
    public void Configure(EntityTypeBuilder<AttendanceDaySummary> builder)
    {
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(s => new { s.TenantId, s.EmployeeId, s.AttendanceDate }).IsUnique();
    }
}
