using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// An attendance vendor robeem can integrate with (study §5.3, §12.1). A tenant connects to one
    /// through an <see cref="AttendanceIntegration"/>.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceProvider), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceProvider : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the stable key the adapter is registered under, e.g. <c>generic-csv</c>. Unique per tenant.</summary>
        [Required]
        [MaxLength(100)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the transports the provider supports: a comma-separated list of
        /// <see cref="AttendanceTransport"/> member names, e.g. <c>FileUpload,RestApi</c>.
        /// </summary>
        [MaxLength(255)]
        public string SupportedTransports { get; set; } = string.Empty;

        public bool SupportsWebhooks { get; set; }

        public bool SupportsIncrementalSync { get; set; }

        public bool IsActive { get; set; } = true;

        #region not mapped
        [NotMapped]
        public override string Title => DisplayName;
        #endregion

        #region method
        public bool SupportsTransport(AttendanceTransport transport) =>
            SupportedTransports
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(transport.ToString(), StringComparer.OrdinalIgnoreCase);
        #endregion
    }
}
