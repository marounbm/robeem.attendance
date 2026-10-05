using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;
using robeem.Contracts.HR.Employees;

namespace robeem.Web.Models;

public sealed class PolicyFormViewModel
{
    /// <summary>Gets or sets the policy id; null while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    public PolicyInput Input { get; set; } = new();

    /// <summary>Gets or sets the schedules that have no policy yet (create only; an existing policy's scope is fixed).</summary>
    [BindNever]
    public IReadOnlyList<ScheduleOption> Schedules { get; set; } = Array.Empty<ScheduleOption>();

    /// <summary>Gets or sets a value indicating whether the tenant-wide default exists already, so creating another is not offered.</summary>
    [BindNever]
    public bool DefaultExists { get; set; }

    /// <summary>Gets or sets the name of the scope of an existing policy: the tenant, or the schedule's name.</summary>
    [BindNever]
    public string ScopeName { get; set; } = string.Empty;
}
