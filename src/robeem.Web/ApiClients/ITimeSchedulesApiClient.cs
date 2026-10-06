using robeem.Contracts.HR.Schedules;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's time schedule definitions (<c>api/time-schedules</c>).</summary>
public interface ITimeSchedulesApiClient
{
    Task<IReadOnlyList<TimeScheduleListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One schedule in full, or null when there is none with that id.</summary>
    Task<TimeScheduleDetail?> GetAsync(int id, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(TimeScheduleInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, TimeScheduleInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<ApiSaveResult> AddShiftAsync(int id, ScheduleShiftInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateShiftAsync(int id, int shiftId, ScheduleShiftInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteShiftAsync(int id, int shiftId, CancellationToken cancellationToken);

    Task<ApiSaveResult> AddTeamAsync(int id, ScheduleTeamInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> RenameTeamAsync(int id, int teamId, ScheduleTeamInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteTeamAsync(int id, int teamId, CancellationToken cancellationToken);

    Task<ApiSaveResult> SaveRosterAsync(int id, int teamId, ScheduleRosterInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> SetTeamMembersAsync(int id, int teamId, ScheduleMembersInput input, CancellationToken cancellationToken);
}
