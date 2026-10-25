using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceDeviceConfiguration : IEntityTypeConfiguration<AttendanceDevice>
{
    public void Configure(EntityTypeBuilder<AttendanceDevice> builder)
    {
        builder.HasOne(d => d.DeviceType).WithMany().HasForeignKey(d => d.DeviceTypeId);

        // The connection a device is reached through, when it is not the integration's default. Restrict, like every foreign key.
        builder.HasOne(d => d.Connection).WithMany().HasForeignKey(d => d.ConnectionId).OnDelete(DeleteBehavior.Restrict);

        // Normalization resolves a punch's device by the vendor's id within its integration.
        builder.HasIndex(d => new { d.TenantId, d.IntegrationId, d.ExternalDeviceId }).IsUnique();
    }
}
