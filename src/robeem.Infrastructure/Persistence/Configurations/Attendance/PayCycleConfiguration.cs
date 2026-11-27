using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class PayCycleConfiguration : IEntityTypeConfiguration<PayCycle>
{
    public void Configure(EntityTypeBuilder<PayCycle> builder)
    {
        builder.Property(c => c.Frequency).HasConversion<string>().HasMaxLength(32);
        builder.HasOne(c => c.Employee).WithMany().HasForeignKey(c => c.EmployeeId);

        // One cycle per employee, and one tenant-wide default (EmployeeId null). A plain unique index would allow any number of defaults,
        // because NULLs are distinct in it.
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId })
            .IsUnique()
            .HasFilter($"\"{nameof(PayCycle.EmployeeId)}\" IS NOT NULL");
        builder.HasIndex(c => c.TenantId)
            .IsUnique()
            .HasFilter($"\"{nameof(PayCycle.EmployeeId)}\" IS NULL");
    }
}
