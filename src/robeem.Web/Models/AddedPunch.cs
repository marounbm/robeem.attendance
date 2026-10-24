namespace robeem.Web.Models;

/// <summary>A punch the employee adds on the Correct page because it is missing.</summary>
public sealed class AddedPunch
{
    public string? EventType { get; set; }

    public TimeOnly? Time { get; set; }

    /// <summary>Gets or sets a value indicating whether the punch was after midnight, on the day after the one being corrected. Only for a shift that ends then.</summary>
    public bool AfterMidnight { get; set; }
}
