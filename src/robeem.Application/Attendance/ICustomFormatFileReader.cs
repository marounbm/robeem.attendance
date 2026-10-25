using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Reads an uploaded file by an administrator-defined <see cref="AttendancePayloadFormat"/>. Like an adapter it
/// only picks fields out; it interprets nothing. The whole row is the raw payload, and the shared
/// <c>IRawPayloadSanitizer</c> removes biometric-looking columns when the rows are stored (CLAUDE.md §1.10).
/// </summary>
public interface ICustomFormatFileReader
{
    /// <summary>Returns one transaction per data row. Throws <see cref="InvalidDataException"/> when the file cannot be read as the format says.</summary>
    Task<IReadOnlyList<ExternalAttendanceTransaction>> ReadAsync(AttendancePayloadFormat format, string path, CancellationToken cancellationToken);
}
