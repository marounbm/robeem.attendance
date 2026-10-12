using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Builds the event interpreter of a custom file format: the timestamp patterns and the type labels
/// the administrator defined. Used by normalization in place of the provider's keyed interpreter for an
/// integration that reads its uploads by a format.
/// </summary>
public interface ICustomFormatInterpreterFactory
{
    /// <summary>Creates the interpreter. The format's <see cref="AttendanceFileFormat.EventCodes"/> must be loaded.</summary>
    IAttendanceEventInterpreter Create(AttendanceFileFormat format);
}
