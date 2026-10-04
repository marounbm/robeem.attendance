using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.HR;

public sealed class TimeScheduleEmployeeConfiguration : IEntityTypeConfiguration<TimeScheduleEmployee>
{
    public void Configure(EntityTypeBuilder<TimeScheduleEmployee> builder)
    {
        // The shared entity has TimeScheduleId without a navigation property; declare the FK explicitly.
        builder.HasOne<TimeSchedule>()
            .WithMany()
            .HasForeignKey(e => e.TimeScheduleId);

        // An employee follows exactly one time schedule, so schedule resolution is deterministic.
        builder.HasIndex(RobeemDbContext.TenantIdProperty, nameof(TimeScheduleEmployee.EmployeeId)).IsUnique();
    }
}
