using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> builder)
    {
        builder.Property(c => c.CorrectedEventType).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.ApprovalWorkflow).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.ManagerDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.HrDecision).HasConversion<string>().HasMaxLength(32);

        // One correction at a time per exception. The service checks the exception's status first;
        // this index is the backstop for two proposals racing. A rejected correction frees the
        // exception for a new one.
        builder.HasIndex(c => new { c.TenantId, c.AttendanceExceptionId })
            .IsUnique()
            .HasFilter($"\"{nameof(AttendanceCorrection.Status)}\" IN ('{nameof(AttendanceCorrectionStatus.Pending)}', '{nameof(AttendanceCorrectionStatus.Approved)}')");

        // The engine reads an employee's applied corrections around the day it processes; approvers list the pending ones.
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId, c.ProposedValue });
        builder.HasIndex(c => new { c.TenantId, c.Status });
    }
}
