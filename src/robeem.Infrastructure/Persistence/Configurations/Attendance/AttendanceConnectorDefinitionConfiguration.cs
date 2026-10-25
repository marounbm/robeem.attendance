using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceConnectorDefinitionConfiguration : IEntityTypeConfiguration<AttendanceConnectorDefinition>
{
    public void Configure(EntityTypeBuilder<AttendanceConnectorDefinition> builder)
    {
        builder.HasIndex(c => new { c.TenantId, c.ConnectorKey }).IsUnique();
        builder.HasIndex(c => c.VendorId);

        // Enums are stored by name, so the column stays readable and reordering members is safe.
        builder.Property(c => c.EncryptionKind).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(c => c.Vendor).WithMany().HasForeignKey(c => c.VendorId);
        builder.HasOne(c => c.DefaultPayloadFormat).WithMany().HasForeignKey(c => c.DefaultPayloadFormatId);
        builder.HasMany(c => c.Transports).WithOne(t => t.Connector).HasForeignKey(t => t.ConnectorId);
    }
}
