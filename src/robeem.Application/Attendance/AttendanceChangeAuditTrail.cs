using robeem.Application.Common;

namespace robeem.Application.Attendance;

/// <summary>Writes the entries of <see cref="IChangeAuditTrail"/> into the attendance audit trail.</summary>
public sealed class AttendanceChangeAuditTrail : IChangeAuditTrail
{
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceChangeAuditTrail(IAttendanceAuditTrail audit)
    {
        _audit = audit;
    }

    public void Record(string action, string entityType, int? entityId, string? details = null) =>
        _audit.Record(action, entityType, entityId, details);
}
