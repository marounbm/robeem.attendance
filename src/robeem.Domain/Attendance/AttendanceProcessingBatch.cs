using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// One ingestion, normalization, processing or reprocessing run (study §6.3, §12.4). Every
    /// <see cref="RawAttendanceTransaction"/> records the batch that imported it.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceProcessingBatch), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceProcessingBatch : SystemEntity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the integration the run belongs to; null for a reprocess run that spans integrations.</summary>
        public int? IntegrationId { get; set; }

        public AttendanceIntegration? Integration { get; set; }

        /// <summary>Gets or sets the connection the run read from; null for a run with none (reprocessing) or one from before connections existed.</summary>
        public int? ConnectionId { get; set; }

        public AttendanceConnection? Connection { get; set; }

        /// <summary>Gets or sets the payload format the run read its file by; null when it used none (an adapter) or the run predates the record.</summary>
        public int? PayloadFormatId { get; set; }

        public AttendancePayloadFormat? PayloadFormat { get; set; }

        public AttendanceBatchType BatchType { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.Now;

        public DateTime? CompletedAt { get; set; }

        public AttendanceBatchStatus Status { get; set; } = AttendanceBatchStatus.Running;

        public int RecordsIngested { get; set; }

        public int RecordsFailed { get; set; }

        public string? ErrorSummary { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => PartitionName;
        #endregion
    }
}
