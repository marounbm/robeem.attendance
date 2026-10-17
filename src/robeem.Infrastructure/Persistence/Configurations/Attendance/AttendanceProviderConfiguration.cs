using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceProviderConfiguration : IEntityTypeConfiguration<AttendanceProvider>
{
    public void Configure(EntityTypeBuilder<AttendanceProvider> builder)
    {
        builder.HasIndex(p => new { p.TenantId, p.ProviderKey }).IsUnique();

        // The column the transports were stored in before they were a table.
        builder.Property(p => p.LegacySupportedTransports).HasColumnName("SupportedTransports").HasMaxLength(255);
        builder.HasOne(p => p.DefaultFileFormat).WithMany().HasForeignKey(p => p.DefaultFileFormatId);
        builder.HasMany(p => p.Transports).WithOne(t => t.Provider).HasForeignKey(t => t.ProviderId);
    }
}
