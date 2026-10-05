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
