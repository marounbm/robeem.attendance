using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Outcome of a review decision. <see cref="Errors"/> is keyed by the request's property names, or <c>recordId</c>.</summary>
public sealed class ReviewResult
{
    private ReviewResult(int? decisionId, AttendanceReviewOutcome? outcome, AttendanceRecordStatus? recordStatus, bool notFound, IReadOnlyDictionary<string, string> errors)
    {
        DecisionId = decisionId;
        Outcome = outcome;
        RecordStatus = recordStatus;
        NotFound = notFound;
        Errors = errors;
    }

    public int? DecisionId { get; }

    public AttendanceReviewOutcome? Outcome { get; }

    public AttendanceRecordStatus? RecordStatus { get; }

    public bool NotFound { get; }

    public IReadOnlyDictionary<string, string> Errors { get; }

    public bool Succeeded => !NotFound && Errors.Count == 0;

    public static ReviewResult Success(AttendanceReviewDecision decision, AttendanceRecordStatus recordStatus) =>
        new(decision.Id, decision.Outcome, recordStatus, false, new Dictionary<string, string>());

    public static ReviewResult Missing() => new(null, null, null, true, new Dictionary<string, string>());

    public static ReviewResult Invalid(string field, string message) =>
        new(null, null, null, false, new Dictionary<string, string> { [field] = message });
}
