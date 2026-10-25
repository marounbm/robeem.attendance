namespace robeem.Contracts.Attendance;

/// <summary>The outcome of testing a connection. The message is one the server chose: never an exception text and never what the other end answered.</summary>
public sealed class ConnectionTestResult
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public DateTime TestedAt { get; init; }
}
