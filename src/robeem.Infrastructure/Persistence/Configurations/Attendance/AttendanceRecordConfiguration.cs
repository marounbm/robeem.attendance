using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.ActualCheckInSource).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.ActualCheckOutSource).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.ActualBreakOutSource).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.ActualBreakInSource).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(r => r.SupersededByRecord)
            .WithMany()
            .HasForeignKey(r => r.SupersededByRecordId);

        // One row per version. Two concurrent recalculations of the same shift would both write the
        // same next version, so one of them fails here instead of both becoming current.
        builder.HasIndex(r => new { r.TenantId, r.EmployeeId, r.AttendanceDate, r.TimeScheduleShiftId, r.Version }).IsUnique();

        // Processing, review and payroll read the current versions of an employee's days.
        builder.HasIndex(r => new { r.TenantId, r.EmployeeId, r.AttendanceDate, r.IsCurrent });
    }
}
