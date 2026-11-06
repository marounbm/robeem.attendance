using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceApprovalConfiguration : IEntityTypeConfiguration<AttendanceApproval>
{
    public void Configure(EntityTypeBuilder<AttendanceApproval> builder)
    {
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Workflow).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.ManagerDecision).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.HrDecision).HasConversion<string>().HasMaxLength(32);

        // One pending review approval per record version. A decided one stays as history: a record an exception reopens after it was
        // approved is reviewed again, with a new approval.
        builder.HasIndex(a => new { a.TenantId, a.AttendanceRecordId })
            .IsUnique()
            .HasFilter($"\"{nameof(AttendanceApproval.AttendanceRecordId)}\" IS NOT NULL AND \"{nameof(AttendanceApproval.Status)}\" = '{nameof(AttendanceApprovalStatus.Pending)}'");

        // The approvers' queue: what is pending, of which kind, oldest day first. Payroll reads a period's approved reviews the same way.
        builder.HasIndex(a => new { a.TenantId, a.Status, a.Type, a.AttendanceDate });

        // Several foreign keys to Profile: the employee it is about, who asked, and who decided each step.
        builder.HasOne(a => a.Employee).WithMany().HasForeignKey(a => a.EmployeeId);
        builder.HasOne(a => a.RequestedByEmployee).WithMany().HasForeignKey(a => a.RequestedByEmployeeId);
        builder.HasOne(a => a.ManagerDecidedBy).WithMany().HasForeignKey(a => a.ManagerDecidedById);
        builder.HasOne(a => a.HrDecidedBy).WithMany().HasForeignKey(a => a.HrDecidedById);
        builder.HasOne(a => a.AttendanceRecord).WithMany().HasForeignKey(a => a.AttendanceRecordId);
    }
}
