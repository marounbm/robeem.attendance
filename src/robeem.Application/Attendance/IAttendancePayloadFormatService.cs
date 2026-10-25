using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>Defines the custom file formats an integration can read its manual uploads by.</summary>
public interface IAttendancePayloadFormatService
{
    Task<IReadOnlyList<PayloadFormatListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The active formats, by name, for the integration form.</summary>
    Task<IReadOnlyList<PayloadFormatOption>> ListOptionsAsync(CancellationToken cancellationToken);

    Task<PayloadFormatInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<SaveResult> CreateAsync(PayloadFormatInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateAsync(int id, PayloadFormatInput input, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a sample file by a format that need not be saved and returns its first rows as understood.
    /// Nothing is stored. An invalid format, or a file that cannot be read by it, is answered in
    /// <see cref="PayloadFormatPreview.Problem"/> rather than thrown.
    /// </summary>
    Task<PayloadFormatPreview> PreviewAsync(PayloadFormatInput input, string filePath, CancellationToken cancellationToken);
}
