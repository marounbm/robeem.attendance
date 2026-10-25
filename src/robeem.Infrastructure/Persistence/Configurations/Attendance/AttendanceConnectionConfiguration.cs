using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceConnectionConfiguration : IEntityTypeConfiguration<AttendanceConnection>
{
    public void Configure(EntityTypeBuilder<AttendanceConnection> builder)
    {
        // Enums are stored by name, so the column stays readable and reordering members is safe.
        builder.Property(c => c.Transport).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.AuthMethod).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(c => c.Integration).WithMany(i => i.Connections).HasForeignKey(c => c.IntegrationId);
        builder.HasIndex(c => new { c.TenantId, c.IntegrationId, c.Name }).IsUnique();

        // Exactly one default per integration: a second default is refused by the database, not just by the service.
        builder.HasIndex(c => new { c.TenantId, c.IntegrationId })
            .IsUnique()
            .HasDatabaseName("IX_AttendanceConnection_TenantId_IntegrationId_Default")
            .HasFilter($"\"{nameof(AttendanceConnection.IsDefault)}\" = TRUE");
    }
}
