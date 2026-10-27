using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class DayCorrectionViewModel
{
    /// <summary>Gets the types a punch can have or be given.</summary>
    public static IReadOnlyList<string> EventTypes { get; } = ["CheckIn", "BreakIn", "BreakOut", "CheckOut"];

    public required DayCorrectionView Day { get; init; }

    public required DayCorrectionForm Form { get; init; }
}
