using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.HR.Employees;
using robeem.Contracts.HR.Leaves;

namespace robeem.Web.Models;

/// <summary>Adding or editing one leave, or (while no employee is chosen) finding the employee it is for.</summary>
public sealed class LeaveFormViewModel
{
    /// <summary>Gets or sets the leave id; <c>null</c> while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    public LeaveInput Input { get; set; } = new();

    [BindNever]
    public string EmployeeNumber { get; set; } = string.Empty;

    [BindNever]
    public string EmployeeName { get; set; } = string.Empty;

    /// <summary>Gets or sets what was typed in the employee search box.</summary>
    [BindNever]
    public string? Search { get; set; }

    /// <summary>Gets or sets the employees the search found; shown while no employee is chosen.</summary>
    [BindNever]
    public IReadOnlyList<EmployeeLookupItem> Matches { get; set; } = Array.Empty<EmployeeLookupItem>();

    public bool HasEmployee => Input.EmployeeId > 0;
}
