using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendancePayloadFormatConfiguration : IEntityTypeConfiguration<AttendancePayloadFormat>
{
    public void Configure(EntityTypeBuilder<AttendancePayloadFormat> builder)
    {
        // Enums are stored by name, so the column stays readable and reordering members is safe.
        builder.Property(f => f.PayloadKind).HasConversion<string>().HasMaxLength(32);

        // A name has one row per version.
        builder.HasIndex(f => new { f.TenantId, f.Name, f.Version }).IsUnique();
        builder.HasOne(f => f.Supersedes).WithMany().HasForeignKey(f => f.SupersedesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(f => f.EventCodes).WithOne(c => c.PayloadFormat).HasForeignKey(c => c.PayloadFormatId);
        builder.HasOne(f => f.Connector).WithMany().HasForeignKey(f => f.ConnectorId);
    }
}
