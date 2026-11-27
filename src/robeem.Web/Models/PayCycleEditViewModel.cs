using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The form for one employee's own pay cycle.</summary>
public sealed class PayCycleEditViewModel
{
    public int EmployeeId { get; init; }

    public string EmployeeLabel { get; init; } = string.Empty;

    public PayCycleInput Input { get; init; } = new();

    /// <summary>Gets a value indicating whether the employee already has a cycle of their own (which can then be removed).</summary>
    public bool HasOwn { get; init; }
}
