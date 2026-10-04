using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Configurations.HR;

public sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.HasIndex(p => new { p.TenantId, p.EmployeeNumber }).IsUnique();
        builder.HasIndex(p => new { p.LastName, p.FirstName });
    }
}
