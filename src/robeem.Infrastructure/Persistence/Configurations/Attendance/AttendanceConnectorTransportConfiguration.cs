using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceConnectorTransportConfiguration : IEntityTypeConfiguration<AttendanceConnectorTransport>
{
    public void Configure(EntityTypeBuilder<AttendanceConnectorTransport> builder)
    {
        builder.HasIndex(t => new { t.TenantId, t.ConnectorId, t.TransportId }).IsUnique();
        builder.HasOne(t => t.Transport).WithMany().HasForeignKey(t => t.TransportId);
    }
}
