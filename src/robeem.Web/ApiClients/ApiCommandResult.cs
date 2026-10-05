namespace robeem.Web.ApiClients;

/// <summary>
/// The outcome of an action the user asked for (propose, decide, close...): it worked, with what
/// robeem.Api returned, or it was refused with messages to show. A refusal is an answer, not an
/// error: "the exception is still open" is something to tell the user, not to throw.
/// </summary>
public sealed record ApiCommandResult<T>(bool Succeeded, T? Value, IReadOnlyList<string> Messages)
    where T : class
{
    public string MessageText => string.Join(" ", Messages);

    public static ApiCommandResult<T> Ok(T value) => new(true, value, []);

    public static ApiCommandResult<T> Refused(IReadOnlyList<string> messages) => new(false, null, messages);
}
