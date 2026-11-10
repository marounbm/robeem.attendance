namespace robeem.Application.HR.Absence;

/// <summary>
/// The excused time of a set of employees over a range: the tenant's holidays, which apply to
/// everyone and are held once, and each employee's leaves.
/// </summary>
public sealed class AbsenceCalendar
{
    public static readonly AbsenceCalendar Empty = new([], new Dictionary<int, List<ExcusedPeriod>>());

    private readonly IReadOnlyList<ExcusedPeriod> _holidays;
    private readonly IReadOnlyDictionary<int, List<ExcusedPeriod>> _leaves;

    public AbsenceCalendar(IReadOnlyList<ExcusedPeriod> holidays, IReadOnlyDictionary<int, List<ExcusedPeriod>> leaves)
    {
        _holidays = holidays;
        _leaves = leaves;
    }

    /// <summary>Gets the holidays in the range, whoever works.</summary>
    public IReadOnlyList<ExcusedPeriod> Holidays => _holidays;

    /// <summary>Gets the employee's excused time in the range: the holidays and their own leaves, by start.</summary>
    public IReadOnlyList<ExcusedPeriod> For(int employeeId) =>
        _leaves.TryGetValue(employeeId, out var leaves)
            ? _holidays.Concat(leaves).OrderBy(p => p.Start).ThenBy(p => p.End).ToList()
            : _holidays;

    /// <summary>Gets whether the employee has a whole-day entry (leave or holiday) on <paramref name="date"/>.</summary>
    public bool IsWholeDayExcused(int employeeId, DateTime date) =>
        For(employeeId).Any(p => p.IsWholeDay && p.Date == date.Date);
}
