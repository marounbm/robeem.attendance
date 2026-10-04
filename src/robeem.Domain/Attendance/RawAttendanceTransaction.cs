using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// The vendor-exact copy of one punch (study §6.2). Stored for every event, valid or not, and
    /// never changed afterwards (CLAUDE.md §1.1, §1.2): <c>RobeemDbContext</c> rejects a delete, and
    /// any update other than the bookkeeping columns listed in <see cref="MutableProperties"/>.
    /// </summary>
    /// <remarks>
    /// Vendor-supplied strings have no length limit, so an oversized value can never prevent the
    /// row from being stored.
    /// </remarks>
    [Serializable]
    [Table(nameof(RawAttendanceTransaction), Schema = nameof(SmartEnum.Module.Attendance))]
    public class RawAttendanceTransaction : SystemEntity, ITenantScoped
    {
        /// <summary>
        /// The only columns normalization and retention may change after insert. Everything the
        /// vendor sent, and when and how it was received, is immutable.
        /// </summary>
        public static readonly string[] MutableProperties =
        {
            nameof(ProcessingStatus),
            nameof(ProcessingError),
            nameof(RetentionStatus),
        };

        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int IntegrationId { get; set; }

        public AttendanceIntegration? Integration { get; set; }

        /// <summary>Gets or sets the device id exactly as the vendor sent it.</summary>
        public string? DeviceExternalId { get; set; }

        /// <summary>Gets or sets the resolved device, when it was known at ingestion.</summary>
        public int? DeviceId { get; set; }

        public AttendanceDevice? Device { get; set; }

        public string? ExternalTransactionId { get; set; }

        public string? ExternalEmployeeId { get; set; }

        /// <summary>Gets or sets the timestamp exactly as the vendor sent it, offset included if there was one.</summary>
        [Required]
        public string TransactionTimestampRaw { get; set; } = string.Empty;

        /// <summary>Gets or sets the parsed timestamp; null when it could not be parsed. Stored in UTC.</summary>
        public DateTimeOffset? TransactionTimestampParsed { get; set; }

        public string? TransactionTypeRaw { get; set; }

        public string? DirectionRaw { get; set; }

        /// <summary>Gets or sets the vendor payload as JSON, after <c>IRawPayloadSanitizer</c> removed biometric fields.</summary>
        [Required]
        public string RawPayload { get; set; } = string.Empty;

        /// <summary>Gets or sets the system the record came from, e.g. the provider key or the uploaded file name.</summary>
        [Required]
        [MaxLength(255)]
        public string SourceSystem { get; set; } = string.Empty;

        [Required]
        public int ImportBatchId { get; set; }

        public AttendanceProcessingBatch? ImportBatch { get; set; }

        public string? ApiRequestReference { get; set; }

        /// <summary>Gets or sets when robeem received the record. Stored in UTC.</summary>
        public DateTimeOffset RecordReceivedAt { get; set; } = DateTimeOffset.UtcNow;

        public RawTransactionProcessingStatus ProcessingStatus { get; set; } = RawTransactionProcessingStatus.Pending;

        public string? ProcessingError { get; set; }

        /// <summary>Gets or sets a hash of the record's content. Not unique: raw storage keeps duplicates (CLAUDE.md §1.4).</summary>
        [Required]
        [MaxLength(128)]
        public string RecordHash { get; set; } = string.Empty;

        public RawTransactionRetentionStatus RetentionStatus { get; set; } = RawTransactionRetentionStatus.Active;

        #region not mapped
        [NotMapped]
        public override string Title => PartitionName;
        #endregion
    }
}
