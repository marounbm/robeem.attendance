using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A data-quality or policy problem found while processing an employee's day (study §9.3): a
    /// missing punch, or punches in an invalid order. An exception is a request for a person to
    /// act; it never fixes anything itself, and no punch is ever invented for it (CLAUDE.md §1.5).
    /// </summary>
    /// <remarks>
    /// An exception belongs to an employee, a date and a shift, not to one
    /// <see cref="AttendanceRecord"/> version: records are replaced whenever the day is
    /// recalculated, while the exception stays until it is resolved.
    /// </remarks>
    [Serializable]
    [Table(nameof(AttendanceException), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceException : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        /// <summary>Gets or sets the date of the shift the exception belongs to; for an overnight shift, the date it starts on.</summary>
        public DateTime AttendanceDate { get; set; }

        /// <summary>Gets or sets the scheduled shift; null for work outside any scheduled shift.</summary>
        public int? TimeScheduleShiftId { get; set; }

        public TimeScheduleShift? TimeScheduleShift { get; set; }

        public AttendanceExceptionType ExceptionType { get; set; }

        /// <summary>Gets or sets the punch that is absent. Set for <see cref="AttendanceExceptionType.MissingPunch"/>, otherwise null.</summary>
        public NormalizedEventType? MissingEventType { get; set; }

        /// <summary>Gets or sets the punch that is out of order. Set for <see cref="AttendanceExceptionType.InvalidSequence"/>, otherwise null.</summary>
        public int? RelatedNormalizedTransactionId { get; set; }

        public NormalizedAttendanceTransaction? RelatedNormalizedTransaction { get; set; }

        /// <summary>Gets or sets the type the out-of-order punch of an invalid sequence had when it was found; null for any other exception. Kept here so lists need not read the punch.</summary>
        public NormalizedEventType? RelatedEventType { get; set; }

        /// <summary>Gets or sets when the out-of-order punch happened, as local wall-clock time; null for any other exception.</summary>
        public DateTime? RelatedEventAt { get; set; }

        /// <summary>
        /// Gets or sets the types the out-of-order punch of an <see cref="AttendanceExceptionType.InvalidSequence"/> can be changed to so that this
        /// exception goes away: event type names separated by commas, kept up to date whenever the day is calculated. Empty for any other
        /// exception, and when no other type would settle it.
        /// </summary>
        [MaxLength(100)]
        public string SuggestedEventTypes { get; set; } = string.Empty;

        public AttendanceExceptionStatus Status { get; set; } = AttendanceExceptionStatus.Open;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets or sets when the exception was raised, as local wall-clock time.</summary>
        public DateTime DetectedAt { get; set; } = DateTime.Now;

        /// <summary>Gets or sets when the exception was closed; null while it is not.</summary>
        public DateTime? ResolvedAt { get; set; }

        /// <summary>Gets or sets how the exception was closed; null while it is not.</summary>
        public AttendanceExceptionResolutionMethod? ResolutionMethod { get; set; }

        /// <summary>Gets or sets who accepted the exception (an employee id); null unless it was closed as <see cref="AttendanceExceptionResolutionMethod.Accepted"/>.</summary>
        public int? ResolvedById { get; set; }

        /// <summary>Gets or sets the reason given when the exception was accepted; null otherwise.</summary>
        [MaxLength(1000)]
        public string? ResolutionNote { get; set; }

        #region not mapped
        [NotMapped]
        public bool IsActive => Status != AttendanceExceptionStatus.Closed;

        [NotMapped]
        public override string Title => ExceptionType == AttendanceExceptionType.MissingPunch
            ? $"Missing {MissingEventType} — {AttendanceDate:d}"
            : $"{ExceptionType} — {AttendanceDate:d}";
        #endregion
    }
}
