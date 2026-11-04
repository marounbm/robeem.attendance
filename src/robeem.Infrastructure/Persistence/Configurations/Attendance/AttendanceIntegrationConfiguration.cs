using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceIntegrationConfiguration : IEntityTypeConfiguration<AttendanceIntegration>
{
    public void Configure(EntityTypeBuilder<AttendanceIntegration> builder)
    {
        // Enums are stored by name, so the column stays readable and reordering members is safe.
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.HealthStatus).HasConversion<string>().HasMaxLength(32);

        // The connector is a catalog row of the prime tenant, referred to by key: no foreign key across databases. Files are read by the
        // connector's default payload format; an integration has no format of its own.
        builder.HasIndex(i => i.ConnectorKey);

        // The device an uploaded file's punches are attributed to. Restrict, like every foreign key.
        builder.HasOne(i => i.DefaultDevice).WithMany().HasForeignKey(i => i.DefaultDeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
