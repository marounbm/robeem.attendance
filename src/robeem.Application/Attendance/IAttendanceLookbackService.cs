namespace robeem.Application.Attendance;

/// <summary>
/// The nightly look back over the last days: are the devices still sending, which scheduled days have no calculation yet (a no-show is
/// raised by the engine, never invented here), which days still lack a check-out, and which days a leave or holiday entered since
/// changes (a no-show it excuses closes; a leave removed brings it back). The engine decides; this only finds the days to hand to it.
/// </summary>
public interface IAttendanceLookbackService
{
    /// <summary>Looks at the <paramref name="windowDays"/> days ending yesterday and calculates at most <paramref name="maxDays"/> employee-days again.</summary>
    Task<LookbackResult> RunAsync(int windowDays, int maxDays, CancellationToken cancellationToken);

    /// <summary>
    /// Looks for missing days in any period (of one employee, or of everyone), the way the nightly run does for the last days: the scheduled days
    /// with no calculation yet, on dates when somebody punched, and the days still lacking a check-out. A day is calculated when punches arrive for
    /// it, and the nightly run reaches only the last days, so a period imported later has no no-show for the days nobody punched until this is asked.
    /// Ends at yesterday; calculates at most <paramref name="maxDays"/> employee-days, the rest are reported as deferred. The engine decides; this only hands it the days.
    /// </summary>
    Task<LookbackResult> CheckPeriodAsync(int? employeeId, DateTime from, DateTime to, int maxDays, CancellationToken cancellationToken);

    /// <summary>
    /// Calculates again the days from <paramref name="from"/> to <paramref name="to"/> (of one employee, or of everyone) whose
    /// excused time may have changed: those with an open no-show, a no-show once excused, or leave that the record does not match.
    /// Returns how many employee-days were calculated. For the leave module to call when it saves a leave.
    /// </summary>
    Task<int> ReexamineAbsencesAsync(int? employeeId, DateTime from, DateTime to, CancellationToken cancellationToken);
}
