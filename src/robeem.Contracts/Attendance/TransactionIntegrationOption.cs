namespace robeem.Contracts.Attendance;

/// <summary>An integration to filter the raw and normalized punch screens by.</summary>
public sealed class TransactionIntegrationOption
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}
