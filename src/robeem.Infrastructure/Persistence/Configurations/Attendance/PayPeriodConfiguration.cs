using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class PayPeriodConfiguration : IEntityTypeConfiguration<PayPeriod>
{
    public void Configure(EntityTypeBuilder<PayPeriod> builder)
    {
        builder.HasOne(p => p.Employee).WithMany().HasForeignKey(p => p.EmployeeId);

        // An employee's own periods, and the company-wide ones (EmployeeId null). A plain unique index would not stop two company-wide periods
        // with the same dates, because NULLs are distinct in it, so the company-wide ones have an index of their own.
        builder.HasIndex(p => new { p.TenantId, p.EmployeeId, p.StartDate, p.EndDate }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.StartDate, p.EndDate })
            .IsUnique()
            .HasFilter($"\"{nameof(PayPeriod.EmployeeId)}\" IS NULL")
            .HasDatabaseName("IX_PayPeriod_TenantId_StartDate_EndDate_CompanyWide");
    }
}
