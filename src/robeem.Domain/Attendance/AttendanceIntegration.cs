using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// The tenant's configured connection to an <see cref="AttendanceProvider"/> (study §12.1). It
    /// carries the incremental-sync high-water mark.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceIntegration), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceIntegration : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int ProviderId { get; set; }

        public AttendanceProvider? Provider { get; set; }

        [Required]
        [MaxLength(255)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a reference (key) to the connection settings in secrets storage. Never a
        /// raw secret.
        /// </summary>
        /// <remarks>Resolved to the secret by <c>ISecretStore</c>, under <c>Secrets:{tenantId}:{reference}</c> in configuration (CLAUDE.md §2.8).</remarks>
        [MaxLength(255)]
        public string ConnectionConfigRef { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets how this integration receives attendance. One of the provider's
        /// <see cref="AttendanceProvider.SupportedTransports"/>; only <see cref="AttendanceTransport.FileUpload"/>
        /// integrations are offered on the Upload attendance screen.
        /// </summary>
        public AttendanceTransport Transport { get; set; } = AttendanceTransport.FileUpload;

        /// <summary>
        /// Gets or sets the custom file layout uploads are read by; null for the provider's own layout.
        /// Only meaningful when <see cref="Transport"/> is <see cref="AttendanceTransport.FileUpload"/>.
        /// </summary>
        public int? FileFormatId { get; set; }

        public AttendanceFileFormat? FileFormat { get; set; }

        /// <summary>
        /// Gets or sets the device every punch of an uploaded file is attributed to, ignoring the device
        /// id in the file: a file has no physical device. One of this integration's own devices; only
        /// meaningful when <see cref="Transport"/> is <see cref="AttendanceTransport.FileUpload"/>. Null:
        /// the file's device id is looked up, and an unregistered one is flagged.
        /// </summary>
        public int? DefaultDeviceId { get; set; }

        public AttendanceDevice? DefaultDevice { get; set; }

        /// <summary>
        /// Gets or sets the IANA time zone used to read a punch that carries no UTC offset when its
        /// device gives none (the device is unknown or unregistered). A registered device's own zone
        /// always wins. Null: such a punch fails normalization.
        /// </summary>
        [MaxLength(64)]
        public string? TimeZoneId { get; set; }

        [MaxLength(100)]
        public string? PollingScheduleCron { get; set; }

        /// <summary>Gets or sets the high-water mark of incremental sync. Its shape is defined by the provider.</summary>
        public string? SyncCursor { get; set; }

        public DateTime? LastSuccessfulSyncAt { get; set; }

        public AttendanceIntegrationStatus Status { get; set; } = AttendanceIntegrationStatus.Active;

        public AttendanceIntegrationHealth HealthStatus { get; set; } = AttendanceIntegrationHealth.Unknown;

        #region not mapped
        [NotMapped]
        public override string Title => DisplayName;
        #endregion
    }
}
