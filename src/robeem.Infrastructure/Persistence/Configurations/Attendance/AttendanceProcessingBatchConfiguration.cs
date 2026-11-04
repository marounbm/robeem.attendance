using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceProcessingBatchConfiguration : IEntityTypeConfiguration<AttendanceProcessingBatch>
{
    public void Configure(EntityTypeBuilder<AttendanceProcessingBatch> builder)
    {
        builder.Property(b => b.BatchType).HasConversion<string>().HasMaxLength(32);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasOne(b => b.Connection).WithMany().HasForeignKey(b => b.ConnectionId).OnDelete(DeleteBehavior.Restrict);

        // The payload format is a catalog row of the prime tenant, referred to by name and version: no foreign key across databases.
        builder.HasIndex(b => new { b.PayloadFormatName, b.PayloadFormatVersion });
    }
}
