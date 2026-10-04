using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceDeviceConfiguration : IEntityTypeConfiguration<AttendanceDevice>
{
    public void Configure(EntityTypeBuilder<AttendanceDevice> builder)
    {
        builder.Property(d => d.DeviceType).HasConversion<string>().HasMaxLength(32);

        // Normalization resolves a punch's device by the vendor's id within its integration.
        builder.HasIndex(d => new { d.TenantId, d.IntegrationId, d.ExternalDeviceId }).IsUnique();
    }
}
