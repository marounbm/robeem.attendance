using Microsoft.Extensions.Logging;
using robeem.Application.HR.Absence;

namespace robeem.Application.Attendance;

/// <summary>
/// Calculates the days a saved leave or holiday covers again, so that a no-show it excuses closes and lateness it covers goes away without
/// anyone pressing a button. The leave is already saved when this runs: a failure here is logged, never passed back, and the days can be
/// calculated again from the Attendance screen.
/// </summary>
public sealed class AttendanceAbsenceChangeNotifier : IAbsenceChangeNotifier
{
    // Per round; a holiday of several weeks for a large company takes a few rounds.
    private const int MaxDaysPerRound = 2000;
    private const int MaxRounds = 10;

    private readonly IAttendanceReprocessingService _reprocessing;
    private readonly ILogger<AttendanceAbsenceChangeNotifier> _logger;

    public AttendanceAbsenceChangeNotifier(IAttendanceReprocessingService reprocessing, ILogger<AttendanceAbsenceChangeNotifier> logger)
    {
        _reprocessing = reprocessing;
        _logger = logger;
    }

    public async Task AbsenceChangedAsync(int? employeeId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        try
        {
            var next = from.Date;
            for (var round = 0; next <= to.Date && round < MaxRounds; round++)
            {
                var result = await _reprocessing.ReprocessRangeAsync(next, to, employeeId, MaxDaysPerRound, cancellationToken);
                if (result.NextFrom is not { } carryOn)
                {
                    return;
                }

                next = carryOn;
            }

            _logger.LogWarning("A leave or holiday changed on {From:yyyy-MM-dd} to {To:yyyy-MM-dd}, but not every day could be calculated again at once; use Calculate again on the Attendance screen from {Next:yyyy-MM-dd}.", from, to, next);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Calculating the days again after a leave or holiday changed ({From:yyyy-MM-dd} to {To:yyyy-MM-dd}) failed.", from, to);
        }
    }
}
