using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceFileFormatEventCodeConfiguration : IEntityTypeConfiguration<AttendanceFileFormatEventCode>
{
    public void Configure(EntityTypeBuilder<AttendanceFileFormatEventCode> builder)
    {
        builder.Property(c => c.EventType).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(c => new { c.TenantId, c.FileFormatId, c.Code }).IsUnique();
    }
}
