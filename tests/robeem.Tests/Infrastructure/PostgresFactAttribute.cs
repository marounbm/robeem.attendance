namespace robeem.Tests.Infrastructure;

/// <summary>A fact that is skipped, with the reason, when no PostgreSQL server is configured.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROBEEM_TEST_POSTGRES")))
        {
            Skip = "Set ROBEEM_TEST_POSTGRES to a PostgreSQL connection string (a user who may create databases) to run this.";
        }
    }
}
