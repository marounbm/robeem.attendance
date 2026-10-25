using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// The tenant's configured connection to a vendor, made with one of the vendor's
    /// <see cref="AttendanceConnectorDefinition"/>s (study §12.1). It carries the incremental-sync high-water mark.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceIntegration), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceIntegration : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int ConnectorId { get; set; }

        public AttendanceConnectorDefinition? Connector { get; set; }

        [Required]
        [MaxLength(255)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a reference (key) to the secret that decrypts this integration's files, used when its connector's
        /// <see cref="AttendanceConnectorDefinition.EncryptionKind"/> is not <see cref="PayloadEncryptionKind.None"/>. Never a raw
        /// secret; resolved by <c>ISecretStore</c> for one call, apart from the connections' <see cref="AttendanceConnection.SecretRef"/>.
        /// </summary>
        [MaxLength(255)]
        public string PayloadKeyRef { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the file layout uploads are read by; null for the connector's
        /// <see cref="AttendanceConnectorDefinition.DefaultPayloadFormat"/>.
        /// Only meaningful when the default connection's transport is <see cref="AttendanceTransport.FileUpload"/>.
        /// </summary>
        public int? PayloadFormatId { get; set; }

        public AttendancePayloadFormat? PayloadFormat { get; set; }

        /// <summary>
        /// Gets or sets the device every punch of an uploaded file is attributed to, ignoring the device
        /// id in the file: a file has no physical device. One of this integration's own devices; only
        /// meaningful when the default connection's transport is <see cref="AttendanceTransport.FileUpload"/>. Null:
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

        /// <summary>
        /// Gets or sets the integration's connections: where its attendance comes from and how that is authenticated. Exactly one is
        /// the default (<see cref="AttendanceConnection.IsDefault"/>); a device may name another. An integration read without
        /// its connections loaded has none in memory, which readers treat as a default file upload.
        /// </summary>
        public ICollection<AttendanceConnection> Connections { get; set; } = new List<AttendanceConnection>();

        /// <summary>Gets or sets the high-water mark of incremental sync. Its shape is defined by the vendor.</summary>
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
