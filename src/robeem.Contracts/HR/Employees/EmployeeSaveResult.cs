namespace robeem.Contracts.HR.Employees;

/// <summary>Outcome of a create/update. <see cref="Errors"/> is keyed by <see cref="EmployeeInput"/> property name.</summary>
public sealed class EmployeeSaveResult
{
    private EmployeeSaveResult(int? employeeId, bool notFound, IReadOnlyDictionary<string, string> errors)
    {
        EmployeeId = employeeId;
        NotFound = notFound;
        Errors = errors;
    }

    public int? EmployeeId { get; }

    public bool NotFound { get; }

    public IReadOnlyDictionary<string, string> Errors { get; }

    public bool Succeeded => !NotFound && Errors.Count == 0;

    public static EmployeeSaveResult Success(int employeeId) => new(employeeId, false, new Dictionary<string, string>());

    public static EmployeeSaveResult Missing() => new(null, true, new Dictionary<string, string>());

    public static EmployeeSaveResult Invalid(IReadOnlyDictionary<string, string> errors) => new(null, false, errors);
}
