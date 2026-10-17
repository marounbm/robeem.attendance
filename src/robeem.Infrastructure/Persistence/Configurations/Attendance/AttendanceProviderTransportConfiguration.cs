using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceProviderTransportConfiguration : IEntityTypeConfiguration<AttendanceProviderTransport>
{
    public void Configure(EntityTypeBuilder<AttendanceProviderTransport> builder)
    {
        builder.HasIndex(t => new { t.TenantId, t.ProviderId, t.TransportId }).IsUnique();
        builder.HasOne(t => t.Transport).WithMany().HasForeignKey(t => t.TransportId);
    }
}
