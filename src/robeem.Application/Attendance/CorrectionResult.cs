using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Outcome of proposing or deciding a correction. <see cref="Errors"/> is keyed by the property
/// name of the request that holds the problem (<c>ProposeCorrectionRequest</c> or
/// <c>DecideCorrectionRequest</c>).
/// </summary>
public sealed class CorrectionResult
{
    private CorrectionResult(int? correctionId, AttendanceCorrectionStatus? status, AttendanceExceptionStatus? exceptionStatus, bool notFound, IReadOnlyDictionary<string, string> errors)
    {
        CorrectionId = correctionId;
        Status = status;
        ExceptionStatus = exceptionStatus;
        NotFound = notFound;
        Errors = errors;
    }

    public int? CorrectionId { get; }

    public AttendanceCorrectionStatus? Status { get; }

    public AttendanceExceptionStatus? ExceptionStatus { get; }

    public bool NotFound { get; }

    public IReadOnlyDictionary<string, string> Errors { get; }

    public bool Succeeded => !NotFound && Errors.Count == 0;

    public static CorrectionResult Success(AttendanceCorrection correction, AttendanceExceptionStatus exceptionStatus) =>
        new(correction.Id, correction.Status, exceptionStatus, false, new Dictionary<string, string>());

    public static CorrectionResult Missing() => new(null, null, null, true, new Dictionary<string, string>());

    public static CorrectionResult Invalid(string field, string message) =>
        new(null, null, null, false, new Dictionary<string, string> { [field] = message });

    public static CorrectionResult Invalid(IReadOnlyDictionary<string, string> errors) => new(null, null, null, false, errors);
}
