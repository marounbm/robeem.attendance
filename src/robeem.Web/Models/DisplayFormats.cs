using System.Globalization;

namespace robeem.Web.Models;

/// <summary>How the screens write times, durations and dates, so every page does it the same way.</summary>
public static class DisplayFormats
{
    /// <summary>Writes a duration as hours and minutes, e.g. <c>9:23</c>.</summary>
    public static string Hours(TimeSpan duration)
    {
        var total = (int)Math.Round(duration.TotalMinutes);
        return $"{total / 60}:{Math.Abs(total % 60):00}";
    }

    /// <summary>Writes minutes as hours and minutes, e.g. 83 as <c>1:23</c>.</summary>
    public static string Minutes(int minutes) => Hours(TimeSpan.FromMinutes(minutes));

    public static string Date(DateTime date) => date.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Writes a short duration the way a job run needs it: <c>&lt;1 s</c>, <c>12 s</c>, <c>3 min 05 s</c>.</summary>
    public static string Duration(TimeSpan duration) => duration.TotalSeconds < 1
        ? "<1 s"
        : duration.TotalSeconds < 60 ? $"{(int)duration.TotalSeconds} s" : $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s";

    /// <summary>Writes a time with seconds, for jobs that wake every few seconds.</summary>
    public static string DateTimeSeconds(DateTime? time) => time?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "—";

    public static string Time(DateTime? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";

    public static string DateTime(DateTime? time) => time?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—";

    /// <summary>Writes an instant in UTC, e.g. <c>2026-10-05 14:03 UTC</c>.</summary>
    public static string Instant(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>Splits a name such as <c>PendingCorrection</c> into words: <c>Pending correction</c>.</summary>
    public static string Words(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "—";
        }

        var text = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>
    /// The label of a day's status. The engine calls a day <c>PendingReview</c> both when a person must review it and when it is only
    /// waiting for its open exceptions to be settled; only the first is in the approvers' queue, so the second is named for what it waits for.
    /// </summary>
    /// <remarks>
    /// A day whose correction the employee has sent is <c>Pending approval</c> for everyone who sees it (the employee, their manager and HR),
    /// whatever else is open on it, until the approver decides.
    /// </remarks>
    public static string DayStatus(string? status, int exceptionCount, bool pendingApproval = false) =>
        pendingApproval ? "Pending approval" : WaitsForExceptions(status, exceptionCount) ? "Open exceptions" : Words(status);

    /// <summary>The Bootstrap badge colour of a day's status; see <see cref="DayStatus"/>.</summary>
    public static string DayStatusBadge(string? status, int exceptionCount, bool pendingApproval = false) =>
        pendingApproval ? "warning" : WaitsForExceptions(status, exceptionCount) ? "info" : Badge(status);

    /// <summary>Explains a day's status when the name alone would mislead; null when it needs no explanation.</summary>
    public static string? DayStatusHint(string? status, int exceptionCount, bool pendingApproval = false) =>
        pendingApproval
            ? "A correction was sent for this day and waits for the approver's decision (see Approvals)."
            : WaitsForExceptions(status, exceptionCount)
                ? "Not ready for review: its open exceptions come first. Once they are corrected, the day appears in Approvals if it still needs a decision."
                : null;

    /// <summary>The Bootstrap badge colour of a status name.</summary>
    public static string Badge(string? status) => status switch
    {
        "Approved" or "Applied" or "Closed" or "Healthy" or "Succeeded" or "Active" => "success",
        "Rejected" or "Failed" or "Unhealthy" => "danger",
        "Open" or "PendingReview" or "Pending" or "Degraded" or "Missed" => "warning",
        "PendingCorrection" or "Running" => "info",
        _ => "secondary",
    };

    private static bool WaitsForExceptions(string? status, int exceptionCount) => status == "PendingReview" && exceptionCount > 0;
}
