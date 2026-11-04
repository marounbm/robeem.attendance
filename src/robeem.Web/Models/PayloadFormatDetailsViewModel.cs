using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>A payload format as a tenant sees it: read-only, with example files to download.</summary>
public sealed class PayloadFormatDetailsViewModel
{
    public int Id { get; init; }

    public PayloadFormatInput Format { get; init; } = new();

    /// <summary>Gets a value indicating whether the user can also edit the format (a platform administrator in the prime tenant).</summary>
    public bool CanManage { get; init; }
}
