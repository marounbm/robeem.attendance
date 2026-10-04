using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.HR;

public sealed class TimeScheduleLineConfiguration : IEntityTypeConfiguration<TimeScheduleLine>
{
    public void Configure(EntityTypeBuilder<TimeScheduleLine> builder)
    {
        // Mirrors TimeScheduleLine.UniqueId: one row per schedule/team/cycle day/shift.
        builder.HasIndex(
                RobeemDbContext.TenantIdProperty,
                nameof(TimeScheduleLine.TimeScheduleId),
                nameof(TimeScheduleLine.TeamId),
                nameof(TimeScheduleLine.CycleDay),
                nameof(TimeScheduleLine.ShiftId))
            .IsUnique();
    }
}
