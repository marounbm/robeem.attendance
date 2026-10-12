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

    /// <summary>Writes a share as a percentage, or a dash when there was nothing to measure.</summary>
    public static string Percent(double? share) => share is { } value ? $"{value * 100:0.#}%" : "—";

    /// <summary>Writes a delay in seconds as seconds, minutes or hours.</summary>
    public static string Delay(double? seconds) => seconds switch
    {
        null => "—",
        < 90 => $"{seconds:0} s",
        < 5400 => $"{seconds / 60:0.#} min",
        _ => $"{seconds / 3600:0.#} h",
    };

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

    /// <summary>Writes how an integration receives attendance (an AttendanceTransport name) for a person.</summary>
    public static string Transport(string? transport) => transport switch
    {
        "FileUpload" => "Manual file upload",
        "Sftp" => "SFTP drop",
        "RestApi" => "REST API",
        "Webhook" => "Webhook",
        "Database" => "Database",
        "DeviceSdk" => "Device SDK / agent",
        _ => Words(transport),
    };

    /// <summary>Whether the integration pulls from the vendor (so it has a schedule) rather than being pushed to.</summary>
    public static bool PullsOnSchedule(string? transport) => transport is "Sftp" or "RestApi" or "Database" or "DeviceSdk";

    /// <summary>The Bootstrap badge colour of a status name.</summary>
    public static string Badge(string? status) => status switch
    {
        "Approved" or "Applied" or "Closed" or "Healthy" or "Succeeded" or "Active" => "success",
        "Rejected" or "Failed" or "Unhealthy" => "danger",
        "Open" or "PendingReview" or "Pending" or "Degraded" or "Missed" => "warning",
        "PendingCorrection" or "Running" => "info",
        _ => "secondary",
    };
}
