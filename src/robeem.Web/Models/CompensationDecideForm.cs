using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>What the payroll administrator posts from an employee's report: a choice for each line they decided, and optionally one choice for all undecided under time.</summary>
public sealed class CompensationDecideForm
{
    public int EmployeeId { get; set; }

    /// <summary>Gets or sets the first day of the pay period the lines belong to.</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>Gets or sets the view to return to: Last, or Containing with <see cref="Date"/>.</summary>
    public string Mode { get; set; } = CompensationIndexViewModel.Latest;

    public DateTime? Date { get; set; }

    public List<CompensationLineChoice> Lines { get; set; } = [];

    /// <summary>Gets or sets a decision (Ignore or Penalise) for every under time line that has none; empty for none.</summary>
    public string? UndecidedUndertime { get; set; }

    /// <summary>Gets the request: the lines that were given a decision, in the order they were posted.</summary>
    public CompensationDecisionRequest ToRequest() => new()
    {
        EmployeeId = EmployeeId,
        PeriodStart = PeriodStart.Date,
        Lines = Lines
            .Where(l => !string.IsNullOrEmpty(l.Decision))
            .Select(l => new CompensationLineDecision
            {
                Date = l.Date.Date,
                LineKind = l.LineKind,
                Decision = l.Decision!,
                LeaveType = string.IsNullOrEmpty(l.LeaveType) ? null : l.LeaveType,
                Note = string.IsNullOrWhiteSpace(l.Note) ? null : l.Note.Trim(),
            })
            .ToList(),
        UndecidedUndertime = string.IsNullOrEmpty(UndecidedUndertime) ? null : UndecidedUndertime,
    };
}
