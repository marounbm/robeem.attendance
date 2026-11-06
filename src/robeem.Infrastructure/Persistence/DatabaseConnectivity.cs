using Npgsql;

namespace robeem.Infrastructure.Persistence;

/// <summary>Tells a database that cannot be reached from a database that answered with an error of its own.</summary>
public static class DatabaseConnectivity
{
    /// <summary>
    /// True when <paramref name="exception"/>, or anything it wraps, means the database could not be reached or would not let us
    /// in: no route, a refused or dropped connection, a timeout, a server that is starting up or full, or a refused login.
    /// A failing statement (a constraint, a missing table) is not an outage, and neither is an I/O error that Npgsql did not raise.
    /// </summary>
    public static bool IsUnreachable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException postgres:
                    // The server answered. Only its connection-level refusals count.
                    if (IsConnectionSqlState(postgres.SqlState))
                    {
                        return true;
                    }

                    break;
                case NpgsqlException:
                    // Npgsql wraps socket, timeout and DNS failures of the connection in an NpgsqlException of its own.
                    return true;
                case AggregateException aggregate when aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsUnreachable):
                    return true;
            }
        }

        return false;
    }

    // Class 08 connection exceptions, 28 invalid authorization, 53300 too many connections, 57P03 cannot connect now.
    private static bool IsConnectionSqlState(string sqlState)
        => sqlState.StartsWith("08", StringComparison.Ordinal)
            || sqlState.StartsWith("28", StringComparison.Ordinal)
            || sqlState is "53300" or "57P03";
}
