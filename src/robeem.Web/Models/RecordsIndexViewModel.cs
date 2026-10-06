using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class RecordsIndexViewModel
{
    public static IReadOnlyList<string> Statuses { get; } = ["Any", "Calculated", "PendingReview", "Approved", "Rejected"];

    public IReadOnlyList<RecordDayListItem> Items { get; init; } = Array.Empty<RecordDayListItem>();

    /// <summary>Gets the status shown, or <c>Any</c>.</summary>
    public string Status { get; init; } = "Any";

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public bool OnlyWithExceptions { get; init; }
}
