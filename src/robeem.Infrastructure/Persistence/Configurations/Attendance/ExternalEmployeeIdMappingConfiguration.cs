using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class ExternalEmployeeIdMappingConfiguration : IEntityTypeConfiguration<ExternalEmployeeIdMapping>
{
    public void Configure(EntityTypeBuilder<ExternalEmployeeIdMapping> builder)
    {
        builder.HasIndex(m => new { m.TenantId, m.DeviceId, m.ExternalEmployeeId }).IsUnique();
    }
}
