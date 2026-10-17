using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceDeviceConfiguration : IEntityTypeConfiguration<AttendanceDevice>
{
    public void Configure(EntityTypeBuilder<AttendanceDevice> builder)
    {
        // The column the type was stored in before types were a table; the old enum was stored by name.
        builder.Property(d => d.LegacyDeviceType).HasColumnName("DeviceType").HasMaxLength(32);
        builder.HasOne(d => d.DeviceType).WithMany().HasForeignKey(d => d.DeviceTypeId);

        // Normalization resolves a punch's device by the vendor's id within its integration.
        builder.HasIndex(d => new { d.TenantId, d.IntegrationId, d.ExternalDeviceId }).IsUnique();
    }
}
