using Cronos;

namespace robeem.Application.Attendance;

/// <summary>Cron expressions for the scheduled jobs: five fields, or six with seconds first. Evaluated in a time zone (the server's, in practice).</summary>
public static class CronSchedule
{
    public static bool TryParse(string? cron, out CronExpression expression)
    {
        expression = null!;
        if (string.IsNullOrWhiteSpace(cron))
        {
            return false;
        }

        var format = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        try
        {
            expression = CronExpression.Parse(cron, format);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    /// <summary>Gets the first occurrence after <paramref name="after"/>; null when the expression has none (or does not parse).</summary>
    public static DateTimeOffset? Next(string? cron, DateTimeOffset after, TimeZoneInfo zone) =>
        TryParse(cron, out var expression) ? expression.GetNextOccurrence(after, zone) : null;

    /// <summary>
    /// Whether a run is due: the first occurrence after <paramref name="lastRun"/> is not in the
    /// future. Due at once when there has been no run. Never due for an expression that does not parse.
    /// </summary>
    public static bool IsDue(string? cron, DateTimeOffset? lastRun, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!TryParse(cron, out var expression))
        {
            return false;
        }

        return lastRun is not { } last || (expression.GetNextOccurrence(last, zone) is { } next && next <= now);
    }
}
