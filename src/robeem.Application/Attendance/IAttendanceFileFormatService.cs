using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>Defines the custom file formats an integration can read its manual uploads by.</summary>
public interface IAttendanceFileFormatService
{
    Task<IReadOnlyList<FileFormatListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The active formats, by name, for the integration form.</summary>
    Task<IReadOnlyList<FileFormatOption>> ListOptionsAsync(CancellationToken cancellationToken);

    Task<FileFormatInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<SaveResult> CreateAsync(FileFormatInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateAsync(int id, FileFormatInput input, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a sample file by a format that need not be saved and returns its first rows as understood.
    /// Nothing is stored. An invalid format, or a file that cannot be read by it, is answered in
    /// <see cref="FileFormatPreview.Problem"/> rather than thrown.
    /// </summary>
    Task<FileFormatPreview> PreviewAsync(FileFormatInput input, string filePath, CancellationToken cancellationToken);
}
