namespace robeem.Contracts.Attendance;

/// <summary>The answer to a set of compensation decisions: how many were recorded.</summary>
public sealed class CompensationDecidedResponse
{
    public int Decided { get; init; }
}
