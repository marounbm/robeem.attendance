using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A proposed fix to an <see cref="AttendanceException"/> (study §11.2): the missing punch, at
    /// the time the employee says it happened. Additive (CLAUDE.md §1.7): it is a new row that
    /// references the exception, with who asked, why, and who approved. Once <c>Applied</c>, the
    /// processing engine reads it as a punch whose source is <c>Corrected</c>. The raw and
    /// normalized punches are never touched.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceCorrection), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceCorrection : Entity, ITenantScoped, IWorkflowEntity
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the exception this correction was proposed for. Null for a correction of a whole day, which is not tied to one
        /// exception: the day's open exceptions wait for it together.
        /// </summary>
        public int? AttendanceExceptionId { get; set; }

        public AttendanceException? AttendanceException { get; set; }

        /// <summary>
        /// Gets or sets what ties the edits of one request together. A request is decided as a whole: its rows are approved, applied or rejected
        /// at once. A correction proposed for one exception is a request of its own.
        /// </summary>
        public Guid RequestId { get; set; } = Guid.NewGuid();

        /// <summary>Gets or sets the employee whose day is corrected; the exception's employee.</summary>
        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        /// <summary>Gets or sets the date of the exception's shift.</summary>
        public DateTime AttendanceDate { get; set; }

        public AttendanceCorrectionKind Kind { get; set; } = AttendanceCorrectionKind.SupplyPunch;

        /// <summary>
        /// Gets or sets the punch whose type is changed, for <see cref="AttendanceCorrectionKind.ChangePunchType"/>; null otherwise.
        /// The normalized row is never edited: the engine reads this correction as the same punch with another type.
        /// </summary>
        public int? NormalizedTransactionId { get; set; }

        public NormalizedAttendanceTransaction? NormalizedTransaction { get; set; }

        /// <summary>Gets or sets the type the punch had when the change was proposed; null for a supplied punch. For an ignored punch, the type it had.</summary>
        public NormalizedEventType? OriginalEventType { get; set; }

        /// <summary>
        /// Gets or sets the punch the correction supplies (the exception's missing punch), or, for a change of type,
        /// the type the existing punch becomes.
        /// </summary>
        public NormalizedEventType CorrectedEventType { get; set; }

        /// <summary>Gets or sets when the punch happened, as local wall-clock time. For a change of type, the time of the punch that is changed.</summary>
        public DateTime ProposedValue { get; set; }

        /// <summary>Gets or sets the IANA zone <see cref="ProposedValue"/> is in, fixed when the correction is proposed so the instant never shifts.</summary>
        [Required]
        [MaxLength(64)]
        public string TimeZoneId { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string Justification { get; set; } = string.Empty;

        [Required]
        public int RequestedByEmployeeId { get; set; }

        public Profile? RequestedByEmployee { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.Now;

        /// <summary>Gets or sets who must approve. Copied from the policy when the correction is proposed, so a later policy change does not alter a correction already under review.</summary>
        public CorrectionApprovalWorkflow ApprovalWorkflow { get; set; } = CorrectionApprovalWorkflow.Manager;

        public AttendanceCorrectionStatus Status { get; set; } = AttendanceCorrectionStatus.Pending;

        public AttendanceCorrectionDecision? ManagerDecision { get; set; }

        public int? ManagerDecidedById { get; set; }

        public Profile? ManagerDecidedBy { get; set; }

        public DateTime? ManagerDecidedAt { get; set; }

        public AttendanceCorrectionDecision? HrDecision { get; set; }

        public int? HrDecidedById { get; set; }

        public Profile? HrDecidedBy { get; set; }

        public DateTime? HrDecidedAt { get; set; }

        /// <summary>Gets or sets when the correction was folded into the calculation.</summary>
        public DateTime? AppliedAt { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => Kind switch
        {
            AttendanceCorrectionKind.ChangePunchType => $"Correction {OriginalEventType} to {CorrectedEventType} — {AttendanceDate:d}",
            AttendanceCorrectionKind.IgnorePunch => $"Correction ignore {OriginalEventType} — {AttendanceDate:d}",
            _ => $"Correction {CorrectedEventType} — {AttendanceDate:d}",
        };
        #endregion
    }
}
