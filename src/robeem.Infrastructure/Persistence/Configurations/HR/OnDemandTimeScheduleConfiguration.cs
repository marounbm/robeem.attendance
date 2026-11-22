using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.HR;

public sealed class OnDemandTimeScheduleConfiguration : IEntityTypeConfiguration<OnDemandTimeSchedule>
{
    public void Configure(EntityTypeBuilder<OnDemandTimeSchedule> builder)
    {
        builder.HasOne(o => o.TimeSchedule).WithMany().HasForeignKey(o => o.TimeScheduleId);
        builder.HasIndex(o => new { o.TenantId, o.TimeScheduleId }).IsUnique();
    }
}
