using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class ExceptionsIndexViewModel
{
    public static IReadOnlyList<string> Statuses { get; } = ["Active", "Open", "PendingCorrection", "Closed", "All"];

    public IReadOnlyList<ExceptionListItem> Items { get; init; } = Array.Empty<ExceptionListItem>();

    public string Status { get; init; } = "Active";

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }
}
