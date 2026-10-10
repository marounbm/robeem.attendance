using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Implemented by an <see cref="IAttendanceProvider"/> that can describe how its raw data is
/// mapped. The description is built from the adapter's own column lists and its interpreter's
/// tables, so what the mapping screen shows is what the code does.
/// </summary>
public interface IAttendanceMappingGuide
{
    ProviderMappingGuide DescribeMapping();
}
