using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

public sealed class CustomFormatInterpreterFactory : ICustomFormatInterpreterFactory
{
    public IAttendanceEventInterpreter Create(AttendancePayloadFormat format) => new CustomFormatInterpreter(format);
}
