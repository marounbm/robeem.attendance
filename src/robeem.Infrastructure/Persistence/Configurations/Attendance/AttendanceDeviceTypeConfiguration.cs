using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceDeviceTypeConfiguration : IEntityTypeConfiguration<AttendanceDeviceType>
{
    public void Configure(EntityTypeBuilder<AttendanceDeviceType> builder)
    {
        builder.HasIndex(t => new { t.TenantId, t.Name }).IsUnique();
    }
}
