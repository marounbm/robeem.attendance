namespace robeem.Application.Attendance;

/// <summary>
/// The error keys of a refused approval decision. A wrapper over the approval service (the correction or review service) renames
/// <see cref="Approval"/> to the key its own callers know, such as <c>correctionId</c> or <c>recordId</c>.
/// </summary>
public static class ApprovalErrorKeys
{
    public const string Approval = "approvalId";

    public const string Reason = "Reason";

    public const string IsHrDecision = "IsHrDecision";
}
