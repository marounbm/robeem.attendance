using System;
using System.Globalization;

namespace robeem.DataModel
{
    /// <summary>
    /// Static helpers the shared entities depend on. Member names (<c>asTime</c>,
    /// <c>isSystemEntity</c>, ...) are dictated by the shared robeem.DataModel sources and must not
    /// be renamed.
    /// </summary>
    public static class Set
    {
        private static readonly string[] TimeFormats = { @"hh\:mm", @"h\:mm" };

        /// <summary>Converts an <c>"HH:mm"</c> string to a <see cref="DateTime"/> carrying that time of day.</summary>
        public static DateTime asTime(string time) => DateTime.MinValue.Add(ParseTime(time));

        /// <summary>
        /// Hours between two <c>"HH:mm"</c> times. An end earlier than the start is treated as
        /// the next day (overnight shift).
        /// </summary>
        public static double TimeSpan(string startTime, string endTime) => Duration(startTime, endTime).TotalHours;

        /// <summary>Hours between start and end, minus the hours between break start and break end.</summary>
        public static double TimeSpan(string startTime, string endTime, string breakStartTime, string breakEndTime)
            => (Duration(startTime, endTime) - Duration(breakStartTime, breakEndTime)).TotalHours;

        public static bool isSystemEntity(Type type) => typeof(SystemEntity).IsAssignableFrom(type);

        public static bool isWorkflowEntity(Type type) => typeof(IWorkflowEntity).IsAssignableFrom(type);

        public static bool IsValidTime(string? time) => TryParseTime(time, out _);

        public static global::System.TimeSpan ParseTime(string time)
        {
            if (!TryParseTime(time, out var value))
            {
                throw new FormatException($"'{time}' is not a valid time; expected HH:mm (00:00 to 23:59).");
            }

            return value;
        }

        private static bool TryParseTime(string? time, out global::System.TimeSpan value)
            => global::System.TimeSpan.TryParseExact(time, TimeFormats, CultureInfo.InvariantCulture, out value);

        private static global::System.TimeSpan Duration(string startTime, string endTime)
        {
            var duration = ParseTime(endTime) - ParseTime(startTime);
            return duration < global::System.TimeSpan.Zero ? duration.Add(global::System.TimeSpan.FromDays(1)) : duration;
        }
    }
}
