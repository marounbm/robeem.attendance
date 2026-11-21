using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceExceptionGroupConfiguration : IEntityTypeConfiguration<AttendanceExceptionGroup>
{
    public void Configure(EntityTypeBuilder<AttendanceExceptionGroup> builder)
    {
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(g => g.ResolutionMethod).HasConversion<string>().HasMaxLength(32);

        // The sync reads an employee's day; the screen lists the groups still waiting for a person.
        // Not unique: the shift is nullable, and closed groups of the same shift stay as history, so the
        // sync matches the one that is not closed in code.
        builder.HasIndex(g => new { g.TenantId, g.EmployeeId, g.AttendanceDate });
        builder.HasIndex(g => new { g.TenantId, g.Status });

        builder.HasMany<AttendanceException>()
            .WithOne(e => e.ExceptionGroup)
            .HasForeignKey(e => e.ExceptionGroupId);
    }
}
