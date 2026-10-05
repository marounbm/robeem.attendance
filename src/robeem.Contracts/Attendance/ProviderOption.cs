namespace robeem.Contracts.Attendance;

public sealed class ProviderOption
{
    public int Id { get; init; }

    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the provider can be used: only an active one has an adapter.</summary>
    public bool IsActive { get; init; }
}
