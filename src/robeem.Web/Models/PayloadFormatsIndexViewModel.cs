using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class PayloadFormatsIndexViewModel
{
    public IReadOnlyList<PayloadFormatListItem> Formats { get; init; } = Array.Empty<PayloadFormatListItem>();
}
