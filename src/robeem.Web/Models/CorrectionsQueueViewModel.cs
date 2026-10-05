using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class CorrectionsQueueViewModel
{
    public CorrectionApprover Awaiting { get; init; }

    /// <summary>Gets a value indicating whether the acting roles include Manager, so its queue can be shown.</summary>
    public bool CanManager { get; init; }

    public bool CanHr { get; init; }

    public IReadOnlyList<CorrectionListItem> Items { get; init; } = Array.Empty<CorrectionListItem>();
}
