using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One employee's compensation report, with the choices to decide its lines.</summary>
public sealed class CompensationEmployeeViewModel
{
    public required CompensationReport Report { get; init; }

    /// <summary>Gets <see cref="CompensationIndexViewModel.Last"/> or <see cref="CompensationIndexViewModel.Containing"/>, to come back to the same view.</summary>
    public string Mode { get; init; } = CompensationIndexViewModel.Latest;

    public DateTime? Date { get; init; }

    /// <summary>Gets the leave types a paid-leave decision can add.</summary>
    public IReadOnlyList<string> PaidLeaveTypes => CompensationDecisionRequest.PaidLeaveTypes;
}
