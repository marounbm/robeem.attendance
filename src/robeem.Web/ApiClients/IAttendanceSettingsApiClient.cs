using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>The attendance settings of the current tenant: the date the punching started.</summary>
public interface IAttendanceSettingsApiClient
{
    Task<AttendanceSettingsItem> GetAsync(CancellationToken cancellationToken);

    Task<ApiSaveResult> SaveAsync(AttendanceSettingsInput input, CancellationToken cancellationToken);
}
