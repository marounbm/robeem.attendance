using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceExceptionConfiguration : IEntityTypeConfiguration<AttendanceException>
{
    public void Configure(EntityTypeBuilder<AttendanceException> builder)
    {
        builder.Property(e => e.ExceptionType).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.MissingEventType).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ResolutionMethod).HasConversion<string>().HasMaxLength(32);

        // Processing reads an employee's day, and review lists the exceptions still waiting for a person.
        // Not unique: the nullable shift and punch columns would make a unique key unreliable, so
        // the engine matches on them when it reconciles.
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.AttendanceDate });
        builder.HasIndex(e => new { e.TenantId, e.Status });
    }
}
