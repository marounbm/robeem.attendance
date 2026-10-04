using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class NormalizedAttendanceTransactionConfiguration : IEntityTypeConfiguration<NormalizedAttendanceTransaction>
{
    public void Configure(EntityTypeBuilder<NormalizedAttendanceTransaction> builder)
    {
        builder.Property(n => n.NormalizedEventType).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.EventSource).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.ConfidenceStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.ProcessingStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.DuplicateStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.ExceptionStatus).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(n => n.DuplicateOfTransaction)
            .WithMany()
            .HasForeignKey(n => n.DuplicateOfTransactionId);

        // The duplicate key (study §10.3). Normalization checks it before inserting; this index is
        // only the backstop for a concurrent race. It is limited to Unique rows because a
        // DuplicateConfirmed row has the same key as the row it duplicates, and must be stored too.
        builder.HasIndex(n => new { n.TenantId, n.EmployeeId, n.DeviceId, n.NormalizedEventType, n.DuplicateBucketTimestampUtc })
            .IsUnique()
            .HasFilter($"\"{nameof(NormalizedAttendanceTransaction.DuplicateStatus)}\" = '{nameof(DuplicateStatus.Unique)}'");
    }
}
