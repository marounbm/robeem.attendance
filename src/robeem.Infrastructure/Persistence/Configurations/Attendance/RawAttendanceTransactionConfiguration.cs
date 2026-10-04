using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class RawAttendanceTransactionConfiguration : IEntityTypeConfiguration<RawAttendanceTransaction>
{
    public void Configure(EntityTypeBuilder<RawAttendanceTransaction> builder)
    {
        builder.Property(r => r.ProcessingStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.RetentionStatus).HasConversion<string>().HasMaxLength(32);

        // Not unique: raw storage is duplicate-tolerant (CLAUDE.md §1.4); the hash only finds repeats.
        builder.HasIndex(r => new { r.TenantId, r.RecordHash });

        // Normalization picks up the pending rows of a batch.
        builder.HasIndex(r => new { r.TenantId, r.ImportBatchId, r.ProcessingStatus });
    }
}
