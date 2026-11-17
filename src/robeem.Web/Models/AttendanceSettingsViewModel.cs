using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class AttendanceSettingsViewModel
{
    public AttendanceSettingsInput Input { get; set; } = new();

    /// <summary>Gets or sets the last day attendance was entered for, shown beside the form.</summary>
    [BindNever]
    public DateTime? LastAttendanceDate { get; set; }
}
