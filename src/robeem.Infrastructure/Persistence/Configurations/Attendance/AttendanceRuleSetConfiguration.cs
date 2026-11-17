using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceRuleSetConfiguration : IEntityTypeConfiguration<AttendanceRuleSet>
{
    public void Configure(EntityTypeBuilder<AttendanceRuleSet> builder)
    {
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.ThresholdsInRules).HasDefaultValue(false);
        builder.Property(s => s.ShortHoursInRules).HasDefaultValue(false);
        builder.HasOne(s => s.TimeSchedule).WithMany().HasForeignKey(s => s.TimeScheduleId);
        builder.HasIndex(s => new { s.TenantId, s.TimeScheduleId, s.Status });
        builder.HasIndex(s => new { s.TenantId, s.Version });
    }
}
