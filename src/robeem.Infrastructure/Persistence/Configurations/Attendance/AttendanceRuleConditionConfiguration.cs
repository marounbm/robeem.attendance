using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceRuleConditionConfiguration : IEntityTypeConfiguration<AttendanceRuleCondition>
{
    public void Configure(EntityTypeBuilder<AttendanceRuleCondition> builder)
    {
        builder.Property(c => c.Fact).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Operator).HasConversion<string>().HasMaxLength(16);
        builder.HasOne(c => c.AttendanceRule).WithMany(r => r.Conditions).HasForeignKey(c => c.AttendanceRuleId);
        builder.HasIndex(c => new { c.TenantId, c.AttendanceRuleId });
    }
}
