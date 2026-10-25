using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Builds the event interpreter of a custom file format: the timestamp patterns and the type labels
/// the administrator defined. Used by normalization in place of the vendor's keyed interpreter for an
/// integration that reads its uploads by a format.
/// </summary>
public interface ICustomFormatInterpreterFactory
{
    /// <summary>Creates the interpreter. The format's <see cref="AttendancePayloadFormat.EventCodes"/> must be loaded.</summary>
    IAttendanceEventInterpreter Create(AttendancePayloadFormat format);
}
