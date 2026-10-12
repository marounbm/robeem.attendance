using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A record of one attendance file somebody uploaded: which file (name, size and SHA-256 of its
    /// bytes), when, by whom, into which integration, and the batch that stored its rows. The file
    /// itself is not kept; its rows are the <see cref="RawAttendanceTransaction"/>s of
    /// <see cref="BatchId"/>. Insert-only: a row is never changed or deleted.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceUploadedFile), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceUploadedFile : SystemEntity, ITenantScoped
    {
        public const int Sha256Length = 64;

        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int IntegrationId { get; set; }

        public AttendanceIntegration? Integration { get; set; }

        /// <summary>Gets or sets the batch that stored the file's rows.</summary>
        [Required]
        public int BatchId { get; set; }

        public AttendanceProcessingBatch? Batch { get; set; }

        /// <summary>Gets or sets the name the file had on the uploader's computer, without a path.</summary>
        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }

        /// <summary>Gets or sets the SHA-256 of the file's bytes, as 64 lower-case hexadecimal characters.</summary>
        [Required]
        [MaxLength(Sha256Length)]
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>Gets or sets when the file was uploaded. An instant, stored in UTC.</summary>
        public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Gets or sets who uploaded it; null when the caller has no employee profile.</summary>
        public int? UploadedById { get; set; }

        public Profile? UploadedBy { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => PartitionName;
        #endregion
    }
}
