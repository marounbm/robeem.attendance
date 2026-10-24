namespace robeem.Web.Models;

/// <summary>The employee's choice for one punch of the day on the Correct page.</summary>
public sealed class PunchChoice
{
    public int TransactionId { get; set; }

    /// <summary>Gets or sets what to do with the punch: empty to keep it, <c>Ignore</c>, or the type to give it (CheckIn, CheckOut, BreakOut, BreakIn).</summary>
    public string? Action { get; set; }
}
