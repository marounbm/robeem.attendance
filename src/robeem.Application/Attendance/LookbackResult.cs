namespace robeem.Application.Attendance;

/// <summary>What one look-back run did.</summary>
/// <param name="From">The first day looked at.</param>
/// <param name="To">The last day looked at (yesterday).</param>
/// <param name="DevicesChecked">How many devices on a pulling or pushing connection were looked at.</param>
/// <param name="SilentDevices">The ones from which nothing arrived in the window.</param>
/// <param name="DaysCalculated">Employee-days calculated again.</param>
/// <param name="DaysFailed">Employee-days that failed and are tried again on the next run.</param>
/// <param name="DaysDeferred">Employee-days over the run's limit, left for the next night.</param>
/// <param name="DatesWithoutData">Dates on which no employee has any punch: the integration is down, not everyone absent, so no missing day is looked for on them.</param>
public sealed record LookbackResult(
    DateTime From,
    DateTime To,
    int DevicesChecked,
    IReadOnlyList<SilentDevice> SilentDevices,
    int DaysCalculated,
    int DaysFailed,
    int DaysDeferred,
    IReadOnlyList<DateTime> DatesWithoutData);
