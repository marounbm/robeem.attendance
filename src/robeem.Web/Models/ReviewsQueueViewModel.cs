using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class ReviewsQueueViewModel
{
    public IReadOnlyList<ReviewListItem> Items { get; init; } = Array.Empty<ReviewListItem>();
}
