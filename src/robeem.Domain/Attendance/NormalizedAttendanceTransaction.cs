using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A vendor-neutral punch, interpreted from one <see cref="RawAttendanceTransaction"/> (study §7.3).
    /// Duplicates are kept as rows too, marked through <see cref="DuplicateStatus"/> (CLAUDE.md §1.4).
    /// </summary>
    [Serializable]
    [Table(nameof(NormalizedAttendanceTransaction), Schema = nameof(SmartEnum.Module.Attendance))]
    public class NormalizedAttendanceTransaction : SystemEntity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the employee; null while the vendor's employee id is unrecognized.</summary>
        public int? EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        /// <summary>Gets or sets the date the punch counts towards (date only).</summary>
        public DateTime AttendanceDate { get; set; }

        public DateTimeOffset EventTimestampUtc { get; set; }

        /// <summary>
        /// Gets or sets the wall-clock time of the punch in <see cref="TimeZoneId"/>. A
        /// <see cref="DateTime"/>, not a <see cref="DateTimeOffset"/>: PostgreSQL cannot store an
        /// offset, so a local DateTimeOffset would come back in UTC.
        /// </summary>
        public DateTime EventTimestampLocal { get; set; }

        /// <summary>Gets or sets the IANA time zone of <see cref="EventTimestampLocal"/>.</summary>
        [Required]
        [MaxLength(64)]
        public string TimeZoneId { get; set; } = string.Empty;

        public NormalizedEventType NormalizedEventType { get; set; } = NormalizedEventType.Unknown;

        /// <summary>Gets or sets the vendor's event type or direction, as received.</summary>
        public string? OriginalEventType { get; set; }

        public int? DeviceId { get; set; }

        public AttendanceDevice? Device { get; set; }

        [Required]
        public int IntegrationId { get; set; }

        public AttendanceIntegration? Integration { get; set; }

        [Required]
        public int RawTransactionId { get; set; }

        public RawAttendanceTransaction? RawTransaction { get; set; }

        public AttendanceEventSource EventSource { get; set; } = AttendanceEventSource.Device;

        public EventConfidenceStatus ConfidenceStatus { get; set; } = EventConfidenceStatus.Certain;

        public NormalizedTransactionProcessingStatus ProcessingStatus { get; set; } = NormalizedTransactionProcessingStatus.Pending;

        public DuplicateStatus DuplicateStatus { get; set; } = DuplicateStatus.Unique;

        public int? DuplicateOfTransactionId { get; set; }

        public NormalizedAttendanceTransaction? DuplicateOfTransaction { get; set; }

        public TransactionExceptionStatus ExceptionStatus { get; set; } = TransactionExceptionStatus.None;

        /// <summary>
        /// Gets or sets <see cref="EventTimestampUtc"/> rounded down to the applicable
        /// <see cref="AttendancePolicy.DuplicateTimeWindowSeconds"/>. Computed by normalization
        /// before the row is added; part of the duplicate key.
        /// </summary>
        public DateTimeOffset DuplicateBucketTimestampUtc { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => PartitionName;
        #endregion
    }
}
