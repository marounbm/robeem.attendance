using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// The one workflow item an approver decides, for both kinds of thing that need a person: a request to correct a day (its edits
    /// are the <see cref="AttendanceCorrection"/> rows that point here) and a record the engine left <c>PendingReview</c>.
    /// It is the single source of truth for the status and the decisions; the correction rows and the record only carry what is
    /// being decided. Additive (CLAUDE.md §1.7): nothing it covers is edited, and a recalculation that changes a reviewed record
    /// writes a new record, which needs a new approval.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceApproval), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceApproval : Entity, ITenantScoped, IWorkflowEntity
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        public AttendanceApprovalType Type { get; set; }

        /// <summary>Gets or sets the employee whose day is decided.</summary>
        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        public DateTime AttendanceDate { get; set; }

        public AttendanceApprovalStatus Status { get; set; } = AttendanceApprovalStatus.Pending;

        /// <summary>
        /// Gets or sets who must decide. For a correction it is copied from the policy when it is proposed, so a later policy change does
        /// not alter a request already under review.
        /// </summary>
        public ApprovalWorkflow Workflow { get; set; } = ApprovalWorkflow.Manager;

        /// <summary>Gets or sets the employee who proposed it. Null for a review, which the system queues.</summary>
        public int? RequestedByEmployeeId { get; set; }

        public Profile? RequestedByEmployee { get; set; }

        /// <summary>Gets or sets when it was proposed, or when the engine queued the review.</summary>
        public DateTime RequestedAt { get; set; } = DateTime.Now;

        public AttendanceCorrectionDecision? ManagerDecision { get; set; }

        public int? ManagerDecidedById { get; set; }

        public Profile? ManagerDecidedBy { get; set; }

        public DateTime? ManagerDecidedAt { get; set; }

        public AttendanceCorrectionDecision? HrDecision { get; set; }

        public int? HrDecidedById { get; set; }

        public Profile? HrDecidedBy { get; set; }

        public DateTime? HrDecidedAt { get; set; }

        /// <summary>Gets or sets the reviewer's reason. Required to reject a review.</summary>
        [MaxLength(1000)]
        public string? Reason { get; set; }

        /// <summary>Gets or sets when a correction was folded into the calculation.</summary>
        public DateTime? AppliedAt { get; set; }

        /// <summary>Gets or sets the record <b>version</b> a review covers. Null for a correction.</summary>
        public int? AttendanceRecordId { get; set; }

        public AttendanceRecord? AttendanceRecord { get; set; }

        /// <summary>Gets or sets the overtime the record had when the review was queued, so a decision says what it covered. Zero for a correction.</summary>
        public int OvertimeMinutes { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => $"{Type} {Status} — {AttendanceDate:d}";
        #endregion
    }
}
