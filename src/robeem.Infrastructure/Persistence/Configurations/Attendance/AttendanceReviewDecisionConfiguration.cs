using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceReviewDecisionConfiguration : IEntityTypeConfiguration<AttendanceReviewDecision>
{
    public void Configure(EntityTypeBuilder<AttendanceReviewDecision> builder)
    {
        builder.Property(d => d.Outcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.DecidedByRole).HasConversion<string>().HasMaxLength(32);

        // One decision per record version (a changed recalculation is a new record, so a new decision).
        builder.HasIndex(d => new { d.TenantId, d.AttendanceRecordId }).IsUnique();

        // Payroll reads a period's approved decisions.
        builder.HasIndex(d => new { d.TenantId, d.AttendanceDate, d.Outcome });

        // Two foreign keys to Profile: the employee it is about and the person who decided.
        builder.HasOne(d => d.Employee).WithMany().HasForeignKey(d => d.EmployeeId);
        builder.HasOne(d => d.DecidedBy).WithMany().HasForeignKey(d => d.DecidedById);
    }
}
