using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Catalog.Configurations;

public sealed class AttendanceVendorConfiguration : IEntityTypeConfiguration<AttendanceVendor>
{
    public void Configure(EntityTypeBuilder<AttendanceVendor> builder)
    {
        builder.HasIndex(p => p.VendorKey).IsUnique();
    }
}
