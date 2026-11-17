using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>The attendance settings of the current tenant. Every change is audited.</summary>
public interface IAttendanceSettingsService
{
    Task<AttendanceSettingsItem> GetAsync(CancellationToken cancellationToken);

    /// <summary>Saves the settings. The start date can't be in the future.</summary>
    Task<SaveResult> SaveAsync(AttendanceSettingsInput input, CancellationToken cancellationToken);
}
