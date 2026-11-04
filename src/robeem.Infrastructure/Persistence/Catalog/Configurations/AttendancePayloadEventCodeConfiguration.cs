using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Catalog.Configurations;

public sealed class AttendancePayloadEventCodeConfiguration : IEntityTypeConfiguration<AttendancePayloadEventCode>
{
    public void Configure(EntityTypeBuilder<AttendancePayloadEventCode> builder)
    {
        builder.Property(c => c.EventType).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(c => new { c.PayloadFormatId, c.Code }).IsUnique();
    }
}
