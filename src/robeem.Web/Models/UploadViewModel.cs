using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The upload screen: the integrations a file can go into, and the one chosen with its format and layout.</summary>
public sealed class UploadViewModel
{
    public IReadOnlyList<UploadTarget> Targets { get; init; } = [];

    /// <summary>Gets the chosen target, or null when none is chosen yet.</summary>
    public UploadTarget? Selected { get; init; }

    /// <summary>Gets the files uploaded so far, newest first.</summary>
    public IReadOnlyList<UploadedFileItem> Uploads { get; init; } = [];
}
