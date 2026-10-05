using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class PayPeriodConfiguration : IEntityTypeConfiguration<PayPeriod>
{
    public void Configure(EntityTypeBuilder<PayPeriod> builder)
    {
        builder.HasIndex(p => new { p.TenantId, p.StartDate, p.EndDate }).IsUnique();
    }
}
