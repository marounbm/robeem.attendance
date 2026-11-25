using Microsoft.EntityFrameworkCore;

namespace robeem.Tests.Infrastructure;

/// <summary>
/// An enum stored by its name must fit its column. SQLite, which the tests run on, does not enforce a length, so a name longer than its column only fails on
/// PostgreSQL (<c>22001: value too long for type character varying</c>) when the row is saved. This checks the model instead, for every such column.
/// </summary>
public sealed class EnumColumnLengthTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void EveryEnumStoredAsText_HasAColumnLongEnoughForItsLongestName()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var tooLong = new List<string>();
        foreach (var entity in db.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!type.IsEnum || property.GetMaxLength() is not { } max)
                {
                    continue;
                }

                // HasConversion<string>() sets the provider type; the converter itself is in the type mapping.
                var stored = property.GetProviderClrType() ?? property.GetTypeMapping().Converter?.ProviderClrType;
                if (stored != typeof(string))
                {
                    continue;
                }

                var longest = Enum.GetNames(type).OrderByDescending(n => n.Length).First();
                if (longest.Length > max)
                {
                    tooLong.Add($"{entity.ClrType.Name}.{property.Name} holds {type.Name}.{longest} ({longest.Length} characters) in a column of {max}");
                }
            }
        }

        Assert.Empty(tooLong);
    }
}
