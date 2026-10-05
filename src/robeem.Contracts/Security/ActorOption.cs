namespace robeem.Contracts.Security;

/// <summary>An employee a developer can choose to act as.</summary>
public sealed class ActorOption
{
    public int Id { get; init; }

    /// <summary>Gets the name to show: the full name with the employee number.</summary>
    public string Name { get; init; } = string.Empty;
}
