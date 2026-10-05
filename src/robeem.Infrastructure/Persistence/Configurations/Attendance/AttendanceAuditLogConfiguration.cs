using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceAuditLogConfiguration : IEntityTypeConfiguration<AttendanceAuditLog>
{
    public void Configure(EntityTypeBuilder<AttendanceAuditLog> builder)
    {
        builder.HasIndex(a => new { a.TenantId, a.Action, a.Id });
        builder.HasIndex(a => new { a.TenantId, a.TargetType, a.TargetId });
    }
}
