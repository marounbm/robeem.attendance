using robeem.HR.Employees;

namespace robeem.Web.Models;

public sealed class EmployeeIndexViewModel
{
    public IReadOnlyList<EmployeeListItem> Employees { get; init; } = Array.Empty<EmployeeListItem>();

    public string? Search { get; init; }

    public bool IncludeInactive { get; init; }
}
