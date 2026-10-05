using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A reviewer's decision on one <see cref="AttendanceRecord"/> that was <c>PendingReview</c>:
    /// overtime that needs pre-approval, unscheduled work, or an incomplete day. Additive
    /// (CLAUDE.md §1.7): the record's calculation is never edited, only its review status moves, and
    /// this row says who moved it, in which role, when and why. It belongs to one record <b>version</b>:
    /// a recalculation that changes the numbers writes a new version, which needs a new decision, so
    /// an approval of 1:23 of overtime never covers 2:00.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceReviewDecision), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceReviewDecision : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int AttendanceRecordId { get; set; }

        public AttendanceRecord? AttendanceRecord { get; set; }

        /// <summary>Gets or sets the employee the record belongs to, copied for queries.</summary>
        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        /// <summary>Gets or sets the record's date, copied for queries.</summary>
        public DateTime AttendanceDate { get; set; }

        public AttendanceReviewOutcome Outcome { get; set; }

        [Required]
        public int DecidedById { get; set; }

        public Profile? DecidedBy { get; set; }

        public AttendanceReviewerRole DecidedByRole { get; set; }

        /// <summary>Gets or sets when it was decided, as local wall-clock time.</summary>
        public DateTime DecidedAt { get; set; } = DateTime.Now;

        /// <summary>Gets or sets why. Required for a rejection.</summary>
        [MaxLength(1000)]
        public string? Reason { get; set; }

        /// <summary>Gets or sets the overtime the record had when it was decided, so the decision says what it covered.</summary>
        public int OvertimeMinutes { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => $"Review {Outcome} — {AttendanceDate:d}";
        #endregion
    }
}
