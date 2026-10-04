using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceProviderConfiguration : IEntityTypeConfiguration<AttendanceProvider>
{
    public void Configure(EntityTypeBuilder<AttendanceProvider> builder)
    {
        builder.HasIndex(p => new { p.TenantId, p.ProviderKey }).IsUnique();
    }
}
