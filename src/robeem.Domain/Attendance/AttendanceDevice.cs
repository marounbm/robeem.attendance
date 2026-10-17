using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A physical or logical punch device, reached through an <see cref="AttendanceIntegration"/>
    /// (study §13.2).
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceDevice), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceDevice : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int IntegrationId { get; set; }

        public AttendanceIntegration? Integration { get; set; }

        /// <summary>Gets or sets the device id as the vendor reports it. Unique per integration.</summary>
        [Required]
        [MaxLength(100)]
        public string ExternalDeviceId { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the device's type. Null only for a device saved before types were a table; the seeder fills it in from <see cref="LegacyDeviceType"/>.</summary>
        public int? DeviceTypeId { get; set; }

        public AttendanceDeviceType? DeviceType { get; set; }

        /// <summary>
        /// Gets or sets the type's name as it was stored before types were a table (the old enum member name).
        /// Read once by the seeder, never written by new code; the column is dropped by a later migration.
        /// </summary>
        [MaxLength(32)]
        public string LegacyDeviceType { get; set; } = string.Empty;

        /// <summary>Gets or sets the machine model, e.g. <c>U350</c>. Empty when not recorded.</summary>
        [MaxLength(100)]
        public string Model { get; set; } = string.Empty;

        /// <summary>Gets or sets the version of the vendor's attendance software that collects from the device. Empty when not recorded.</summary>
        [MaxLength(100)]
        public string SoftwareVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the version of the vendor SDK that software uses to talk to the device. Empty when not recorded.</summary>
        [MaxLength(50)]
        public string SdkVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the IANA time zone the device's local punch times are in, e.g. <c>Asia/Beirut</c>.</summary>
        [Required]
        [MaxLength(64)]
        public string TimeZoneId { get; set; } = string.Empty;

        public DateTime? LastSeenAt { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => Name;
        #endregion
    }
}
