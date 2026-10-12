using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceFileFormatConfiguration : IEntityTypeConfiguration<AttendanceFileFormat>
{
    public void Configure(EntityTypeBuilder<AttendanceFileFormat> builder)
    {
        // Enums are stored by name, so the column stays readable and reordering members is safe.
        builder.Property(f => f.FileKind).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(f => new { f.TenantId, f.Name }).IsUnique();
        builder.HasMany(f => f.EventCodes).WithOne(c => c.FileFormat).HasForeignKey(c => c.FileFormatId);
    }
}
