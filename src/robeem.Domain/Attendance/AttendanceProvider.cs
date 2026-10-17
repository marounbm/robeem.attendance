using System;
using System.Collections.Generic;
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

        /// <summary>Gets or sets the stable key a code adapter is registered under, if the provider has one, e.g. <c>suprema-biostar</c>. Unique per tenant.</summary>
        [Required]
        [MaxLength(100)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Gets or sets the company that makes the devices, e.g. <c>ZKTeco</c>. Empty when not recorded.</summary>
        [MaxLength(255)]
        public string Manufacturer { get; set; } = string.Empty;

        /// <summary>Gets or sets the manufacturer's website, e.g. <c>https://www.zkteco.com</c>. Empty when not recorded.</summary>
        [MaxLength(255)]
        public string WebsiteUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets the transports the provider supports, besides file upload, which any provider can be given.</summary>
        public ICollection<AttendanceProviderTransport> Transports { get; set; } = new List<AttendanceProviderTransport>();

        /// <summary>
        /// Gets or sets the file layout an upload is read by when its integration names none. Null: the provider
        /// has no file layout of its own, so an upload needs a format chosen on the integration.
        /// </summary>
        public int? DefaultFileFormatId { get; set; }

        public AttendanceFileFormat? DefaultFileFormat { get; set; }

        /// <summary>
        /// Gets or sets the transports as they were stored before they were a table: a comma-separated list of
        /// <see cref="AttendanceTransport"/> member names. Read once by the seeder, never written by new code;
        /// the column is dropped by a later migration.
        /// </summary>
        [MaxLength(255)]
        public string LegacySupportedTransports { get; set; } = string.Empty;

        public bool SupportsWebhooks { get; set; }

        public bool SupportsIncrementalSync { get; set; }

        public bool IsActive { get; set; } = true;

        #region not mapped
        [NotMapped]
        public override string Title => DisplayName;
        #endregion
    }
}
