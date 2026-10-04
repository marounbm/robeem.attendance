namespace robeem.Tests;

public static class DateTimeTestExtensions
{
    /// <summary>The date at the given time of day, e.g. <c>monday.At(8, 30)</c>.</summary>
    public static DateTime At(this DateTime date, int hour, int minute) => date.Date.AddHours(hour).AddMinutes(minute);
}
