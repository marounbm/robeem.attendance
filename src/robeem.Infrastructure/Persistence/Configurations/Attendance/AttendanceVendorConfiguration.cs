using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.Attendance;

public sealed class AttendanceVendorConfiguration : IEntityTypeConfiguration<AttendanceVendor>
{
    public void Configure(EntityTypeBuilder<AttendanceVendor> builder)
    {
        builder.HasIndex(p => new { p.TenantId, p.VendorKey }).IsUnique();
    }
}
