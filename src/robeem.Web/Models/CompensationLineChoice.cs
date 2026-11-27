namespace robeem.Web.Models;

/// <summary>The choice made for one line of the report on the form.</summary>
public sealed class CompensationLineChoice
{
    public DateTime Date { get; set; }

    public string LineKind { get; set; } = string.Empty;

    /// <summary>Gets or sets the decision; empty leaves the line as it is.</summary>
    public string? Decision { get; set; }

    public string? LeaveType { get; set; }

    public string? Note { get; set; }
}
