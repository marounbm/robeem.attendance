using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;
using robeem.Contracts.HR.Employees;

namespace robeem.Web.Models;

public sealed class EmployeeFormViewModel
{
    /// <summary>Gets or sets the employee id; <c>null</c> while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    public EmployeeInput Input { get; set; } = new();

    [BindNever]
    public IReadOnlyList<ScheduleOption> Schedules { get; set; } = Array.Empty<ScheduleOption>();

    [BindNever]
    public IReadOnlyList<ManagerOption> Managers { get; set; } = Array.Empty<ManagerOption>();

    /// <summary>Gets or sets recent attendance records for the employee (last 30 days).</summary>
    [BindNever]
    public IReadOnlyList<RecordDayListItem> RecentAttendanceRecords { get; set; } = Array.Empty<RecordDayListItem>();
}
