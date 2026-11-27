using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class PayCompensationDecisionConfiguration : IEntityTypeConfiguration<PayCompensationDecision>
{
    public void Configure(EntityTypeBuilder<PayCompensationDecision> builder)
    {
        builder.Property(d => d.LineKind).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Decision).HasConversion<string>().HasMaxLength(32);
        builder.HasOne(d => d.Employee).WithMany().HasForeignKey(d => d.EmployeeId);
        builder.HasOne(d => d.DecidedBy).WithMany().HasForeignKey(d => d.DecidedById);
        builder.HasIndex(d => new { d.TenantId, d.EmployeeId, d.AttendanceDate });
    }
}
