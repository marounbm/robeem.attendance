using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance.TestEmployees;

/// <summary>How a load ended: done, not allowed by the host, or refused with the rows that are wrong (nothing is loaded then).</summary>
public sealed record TestEmployeeLoadOutcome(TestEmployeeLoadStatus Status, TestEmployeeLoadResult? Result, IReadOnlyList<string> Errors)
{
    public static TestEmployeeLoadOutcome Disabled() => new(TestEmployeeLoadStatus.Disabled, null, []);

    public static TestEmployeeLoadOutcome Invalid(IReadOnlyList<string> errors) => new(TestEmployeeLoadStatus.Invalid, null, errors);

    public static TestEmployeeLoadOutcome Done(TestEmployeeLoadResult result) => new(TestEmployeeLoadStatus.Done, result, []);
}
