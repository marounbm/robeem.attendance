using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace robeem.Tests;

/// <summary>
/// Makes SQLite refuse what PostgreSQL refuses: a <see cref="DateTime"/> of kind UTC written to a
/// `timestamp without time zone` column (every DateTime column, CLAUDE.md §2.8). Without it such a
/// bug only shows against the real database.
/// </summary>
public sealed class UtcDateTimeGuardInterceptor : SaveChangesInterceptor
{
    public static readonly UtcDateTimeGuardInterceptor Instance = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Check(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var offending = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .SelectMany(e => e.Properties.Select(p => (Entry: e, Property: p)))
            .FirstOrDefault(x => x.Property.CurrentValue is DateTime { Kind: DateTimeKind.Utc });

        if (offending.Entry is not null)
        {
            throw new InvalidOperationException(
                $"{offending.Entry.Metadata.ClrType.Name}.{offending.Property.Metadata.Name} is a DateTime of kind UTC; PostgreSQL's `timestamp without time zone` refuses it.");
        }
    }
}
