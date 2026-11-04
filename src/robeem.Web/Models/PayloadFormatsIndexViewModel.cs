using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class PayloadFormatsIndexViewModel
{
    public IReadOnlyList<PayloadFormatListItem> Formats { get; init; } = Array.Empty<PayloadFormatListItem>();

    /// <summary>
    /// Gets a value indicating whether the user can add and change formats: a platform administrator in the prime tenant. Everyone
    /// else sees the formats read-only and downloads example files.
    /// </summary>
    public bool CanManage { get; init; }
}
