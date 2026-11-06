using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A proposed fix to an <see cref="AttendanceException"/> (study §11.2): the missing punch, at
    /// the time the employee says it happened. Additive (CLAUDE.md §1.7): it is a new row that
    /// references the exception, with who asked and why. Whether it is approved is its
    /// <see cref="AttendanceApproval"/>'s: the edits of one request are the rows that point to the same approval, and are decided
    /// together. Once that approval is <c>Applied</c>, the processing engine reads the row as a punch whose source is <c>Corrected</c>.
    /// The raw and normalized punches are never touched.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceCorrection), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceCorrection : Entity, ITenantScoped
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
        /// Gets or sets the approval this edit belongs to. A request is decided as a whole: its rows are approved, applied or rejected
        /// at once. A correction proposed for one exception is a request of its own.
        /// </summary>
        [Required]
        public int AttendanceApprovalId { get; set; }

        public AttendanceApproval? AttendanceApproval { get; set; }

        /// <summary>
        /// Gets or sets whether the approval is still waiting for a decision. Not a second source of truth for the status: it only
        /// lets the database refuse two requests for one exception at the same time (a filtered unique index), and is cleared
        /// together with the approval's status when the request is decided.
        /// </summary>
        public bool IsPending { get; set; } = true;

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

        /// <summary>
        /// Gets or sets the earlier correction whose added punch this one takes back, for <see cref="AttendanceCorrectionKind.CancelSuppliedPunch"/>;
        /// null otherwise. Once this correction is applied the engine no longer reads the added punch; the added correction row stays as it was.
        /// </summary>
        public int? CancelsCorrectionId { get; set; }

        public AttendanceCorrection? CancelsCorrection { get; set; }

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

        #region not mapped
        [NotMapped]
        public override string Title => Kind switch
        {
            AttendanceCorrectionKind.ChangePunchType => $"Correction {OriginalEventType} to {CorrectedEventType} — {AttendanceDate:d}",
            AttendanceCorrectionKind.IgnorePunch => $"Correction ignore {OriginalEventType} — {AttendanceDate:d}",
            AttendanceCorrectionKind.CancelSuppliedPunch => $"Correction delete added {CorrectedEventType} — {AttendanceDate:d}",
            _ => $"Correction {CorrectedEventType} — {AttendanceDate:d}",
        };
        #endregion
    }
}
