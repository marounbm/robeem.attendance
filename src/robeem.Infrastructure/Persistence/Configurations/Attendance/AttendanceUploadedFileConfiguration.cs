using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceUploadedFileConfiguration : IEntityTypeConfiguration<AttendanceUploadedFile>
{
    public void Configure(EntityTypeBuilder<AttendanceUploadedFile> builder)
    {
        // Looking a file up by its hash (has this exact file come before?), and the history newest first.
        builder.HasIndex(f => new { f.TenantId, f.Sha256 });
        builder.HasIndex(f => new { f.TenantId, f.IntegrationId, f.Id });
    }
}
