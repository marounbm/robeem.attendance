using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceSettingsConfiguration : IEntityTypeConfiguration<AttendanceSettings>
{
    public void Configure(EntityTypeBuilder<AttendanceSettings> builder)
    {
        // One row per tenant.
        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}
