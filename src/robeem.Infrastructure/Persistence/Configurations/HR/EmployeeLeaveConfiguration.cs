using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.HR;

public sealed class EmployeeLeaveConfiguration : IEntityTypeConfiguration<EmployeeLeave>
{
    public void Configure(EntityTypeBuilder<EmployeeLeave> builder)
    {
        builder.Property(l => l.LeaveType).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(l => l.Employee).WithMany().HasForeignKey(l => l.EmployeeId);
        builder.HasIndex(l => new { l.TenantId, l.EmployeeId, l.StartDate });
        builder.HasIndex(l => new { l.TenantId, l.StartDate, l.EndDate });
    }
}
