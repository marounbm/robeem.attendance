using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace robeem.Infrastructure.Persistence;

/// <summary>Model conventions shared by the tenant context and the catalog context.</summary>
internal static class PersistenceConventions
{
    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> UtcDateTimeOffsetConverter =
        new(v => v.ToUniversalTime(), v => v);

    /// <summary>Gets the namespace of the catalog context's own configurations and migrations, which the tenant context must not load.</summary>
    public static string CatalogNamespace => typeof(Catalog.CatalogDbContext).Namespace!;

    public static bool IsCatalogType(Type type) =>
        type.Namespace is { } ns && (ns == CatalogNamespace || ns.StartsWith(CatalogNamespace + ".", StringComparison.Ordinal));

    public static void Apply(ModelBuilder modelBuilder, bool isNpgsql)
    {
        // Nothing in robeem is removed by cascade: HR and attendance history must be deleted
        // deliberately, row by row.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }

        // robeem stores local wall-clock times (the shared entities default to DateTime.Now).
        // Npgsql maps DateTime to `timestamp with time zone` by default, which only accepts UTC
        // values, so map every DateTime to `timestamp without time zone` instead. (Provider check
        // because the tests run on SQLite.)
        if (isNpgsql)
        {
            var dateTimeProperties = modelBuilder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?));

            foreach (var property in dateTimeProperties)
            {
                property.SetColumnType("timestamp without time zone");
            }
        }

        // DateTimeOffset is for instants (punch times, receipt times) and is stored in UTC.
        // Npgsql maps it to `timestamp with time zone` and refuses a non-zero offset, so convert on
        // write. Applied on every provider so the SQLite tests store the same values.
        var dateTimeOffsetProperties = modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?));

        foreach (var property in dateTimeOffsetProperties)
        {
            property.SetValueConverter(UtcDateTimeOffsetConverter);
        }
    }
}
