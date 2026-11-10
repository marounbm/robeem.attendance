using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceRuleConfiguration : IEntityTypeConfiguration<AttendanceRule>
{
    public void Configure(EntityTypeBuilder<AttendanceRule> builder)
    {
        builder.Property(r => r.Stage).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Action).HasConversion<string>().HasMaxLength(24);
        builder.Property(r => r.Finding).HasConversion<string>().HasMaxLength(16);
        builder.HasOne(r => r.AttendanceRuleSet).WithMany(s => s.Rules).HasForeignKey(r => r.AttendanceRuleSetId);
        builder.HasIndex(r => new { r.TenantId, r.AttendanceRuleSetId, r.Priority });
    }
}
