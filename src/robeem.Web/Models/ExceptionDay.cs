using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One employee's day that has exceptions: a row of the Exceptions grid.</summary>
public sealed record ExceptionDay(int EmployeeId, string EmployeeName, DateTime Date, IReadOnlyList<ExceptionListItem> Items)
{
    /// <summary>Gets the number of exceptions of the day that are not closed.</summary>
    public int ActiveCount => Items.Count(i => i.Status != "Closed");

    /// <summary>Gets a value indicating whether a correction of the day is waiting for a decision.</summary>
    public bool IsWaiting => Items.Any(i => i.Status == "PendingCorrection");

    /// <summary>Gets a value indicating whether the user can correct the day: it is theirs, an exception is open, and no correction is waiting.</summary>
    public bool CanCorrect => !IsWaiting && Items.Any(i => i.CanCorrect);

    /// <summary>Gets where the day stands: Open, PendingCorrection or Closed.</summary>
    public string Status => Items.Any(i => i.Status == "Open") ? "Open" : IsWaiting ? "PendingCorrection" : "Closed";

    /// <summary>Gets the shifts the exceptions are on.</summary>
    public IReadOnlyList<string> Shifts => Items.Select(i => i.ShiftTitle ?? "Unscheduled work").Distinct().ToList();
}
