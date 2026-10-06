using robeem.Application.Common;
using robeem.Contracts.HR.Schedules;

namespace robeem.Application.HR.Schedules;

/// <summary>
/// Defining time schedules: the repeating pattern, its shifts, its teams and the roster each team
/// works. Attendance calculates every day against these, so every change is audited and a
/// definition that attendance still uses can't be deleted. Nothing already calculated is rewritten:
/// a changed schedule applies to days processed from then on.
/// </summary>
public interface ITimeScheduleService
{
    Task<IReadOnlyList<TimeScheduleListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One schedule in full, or null when it doesn't exist.</summary>
    Task<TimeScheduleDetail?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Creates a schedule with its everyone-team, so employees can be assigned to it at once.</summary>
    Task<SaveResult> CreateAsync(TimeScheduleInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateAsync(int id, TimeScheduleInput input, CancellationToken cancellationToken);

    /// <summary>Deletes a schedule nobody is assigned to, together with its shifts, teams and roster.</summary>
    Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<SaveResult> AddShiftAsync(int scheduleId, ScheduleShiftInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateShiftAsync(int scheduleId, int shiftId, ScheduleShiftInput input, CancellationToken cancellationToken);

    /// <summary>Deletes a shift that no roster uses and no attendance record refers to.</summary>
    Task<SaveResult> DeleteShiftAsync(int scheduleId, int shiftId, CancellationToken cancellationToken);

    Task<SaveResult> AddTeamAsync(int scheduleId, ScheduleTeamInput input, CancellationToken cancellationToken);

    Task<SaveResult> RenameTeamAsync(int scheduleId, int teamId, ScheduleTeamInput input, CancellationToken cancellationToken);

    /// <summary>Deletes a team and its roster; its members go back to the everyone-team. The everyone-team can't be deleted.</summary>
    Task<SaveResult> DeleteTeamAsync(int scheduleId, int teamId, CancellationToken cancellationToken);

    /// <summary>Replaces the whole roster of a team.</summary>
    Task<SaveResult> SaveRosterAsync(int scheduleId, int teamId, ScheduleRosterInput input, CancellationToken cancellationToken);

    /// <summary>Sets who is in a team. Only employees already assigned to the schedule can be; the everyone-team has no chosen members.</summary>
    Task<SaveResult> SetTeamMembersAsync(int scheduleId, int teamId, ScheduleMembersInput input, CancellationToken cancellationToken);
}
