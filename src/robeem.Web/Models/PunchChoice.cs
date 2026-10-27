namespace robeem.Web.Models;

/// <summary>The employee's choice for one punch of the day on the Correct page.</summary>
public sealed class PunchChoice
{
    /// <summary>Gets or sets the device punch the choice is for; null for a punch that a correction added.</summary>
    public int? TransactionId { get; set; }

    /// <summary>Gets or sets the earlier correction that added the punch the choice is for; null for a device punch.</summary>
    public int? CorrectionId { get; set; }

    /// <summary>Gets or sets what to do with the punch: empty to keep it, <c>Ignore</c> (delete it), or the type to give it (CheckIn, CheckOut, BreakOut, BreakIn).</summary>
    public string? Action { get; set; }
}
