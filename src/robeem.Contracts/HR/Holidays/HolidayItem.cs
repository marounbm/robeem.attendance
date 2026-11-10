namespace robeem.Contracts.HR.Holidays;

/// <summary>One public holiday as listed.</summary>
public sealed class HolidayItem
{
    public int Id { get; init; }

    public DateTime Date { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsProvisional { get; init; }
}
