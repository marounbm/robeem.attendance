namespace robeem.Tests;

/// <summary>A clock that says it is <see cref="Local"/>, wall-clock, until a test moves it.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTime local)
    {
        Local = local;
    }

    public DateTime Local { get; set; }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Local, DateTimeKind.Utc));
}
