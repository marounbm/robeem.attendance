using System.Globalization;

namespace robeem.Application.Attendance;

/// <summary>
/// The culture a file format reads month and day names in. Whatever the language, dates are read with the Gregorian
/// calendar: some Arabic cultures default to Hijri, which would silently shift every date by centuries.
/// </summary>
public static class TimestampCultures
{
    /// <summary>Gets a value indicating whether <paramref name="tag"/> is empty (invariant) or a culture the platform knows.</summary>
    public static bool IsValid(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return true;
        }

        try
        {
            CultureInfo.GetCultureInfo(tag.Trim(), predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Gets the culture for <paramref name="tag"/>, with the Gregorian calendar; the invariant culture when empty or unknown.</summary>
    public static CultureInfo Resolve(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            var culture = (CultureInfo)CultureInfo.GetCultureInfo(tag.Trim(), predefinedOnly: true).Clone();
            var gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault();
            if (gregorian is not null)
            {
                culture.DateTimeFormat.Calendar = gregorian;
            }

            return culture;
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
