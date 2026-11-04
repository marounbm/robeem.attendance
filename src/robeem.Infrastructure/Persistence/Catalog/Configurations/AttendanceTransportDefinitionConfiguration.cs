using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Catalog.Configurations;

public sealed class AttendanceTransportDefinitionConfiguration : IEntityTypeConfiguration<AttendanceTransportDefinition>
{
    public void Configure(EntityTypeBuilder<AttendanceTransportDefinition> builder)
    {
        builder.HasIndex(t => t.TransportKey).IsUnique();
    }
}
