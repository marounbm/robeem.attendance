using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class FileFormatsIndexViewModel
{
    public IReadOnlyList<FileFormatListItem> Formats { get; init; } = Array.Empty<FileFormatListItem>();
}
