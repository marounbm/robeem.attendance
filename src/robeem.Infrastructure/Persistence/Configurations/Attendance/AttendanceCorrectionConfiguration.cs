using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> builder)
    {
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.OriginalEventType).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.CorrectedEventType).HasConversion<string>().HasMaxLength(32);

        // One correction at a time per exception. The service checks the exception's status first;
        // this index is the backstop for two proposals racing. A rejected or applied request clears IsPending, which
        // frees the exception for a new one.
        builder.HasIndex(c => new { c.TenantId, c.AttendanceExceptionId })
            .IsUnique()
            .HasFilter($"\"{nameof(AttendanceCorrection.IsPending)}\" = TRUE");

        // The engine reads an employee's applied corrections around the day it processes.
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId, c.ProposedValue });

        // A correction that takes back an added punch names it; the engine finds the cancelled ones through this key.
        builder.HasOne(c => c.CancelsCorrection).WithMany().HasForeignKey(c => c.CancelsCorrectionId);

        // A request is decided as a whole: its rows are found through their approval.
        builder.HasOne(c => c.AttendanceApproval).WithMany().HasForeignKey(c => c.AttendanceApprovalId);
        builder.HasIndex(c => new { c.TenantId, c.AttendanceApprovalId });
    }
}
