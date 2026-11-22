using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance.TestEmployees;

/// <summary>How a deletion (or its preview) ended.</summary>
/// <param name="Status">Done; not allowed by the host; the tenant id was not typed; the file is wrong; or something still needs an employee.</param>
/// <param name="Result">What went (or would go), for <see cref="TestEmployeeDeleteStatus.Done"/>.</param>
/// <param name="Errors">What is wrong, for <see cref="TestEmployeeDeleteStatus.Invalid"/> and <see cref="TestEmployeeDeleteStatus.Blocked"/>.</param>
public sealed record TestEmployeeDeleteOutcome(TestEmployeeDeleteStatus Status, TestEmployeeDeleteResult? Result, IReadOnlyList<string> Errors)
{
    public static TestEmployeeDeleteOutcome Of(TestEmployeeDeleteStatus status) => new(status, null, []);

    public static TestEmployeeDeleteOutcome Refused(TestEmployeeDeleteStatus status, IReadOnlyList<string> errors) => new(status, null, errors);

    public static TestEmployeeDeleteOutcome Done(TestEmployeeDeleteResult result) => new(TestEmployeeDeleteStatus.Done, result, []);
}
