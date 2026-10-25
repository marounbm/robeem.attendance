using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// An attendance vendor robeem can integrate with (study §5.3, §12.1): the company, and what does not change about it.
    /// How its data arrives and is read is described by its <see cref="AttendanceConnectorDefinition"/>s; a tenant connects
    /// through an <see cref="AttendanceIntegration"/> made with one of them.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceVendor), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceVendor : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the stable key of the vendor, e.g. <c>suprema-biostar</c>. Unique per tenant and never changed: raw punches store it as their source system.</summary>
        [Required]
        [MaxLength(100)]
        public string VendorKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Gets or sets the company that makes the devices, e.g. <c>ZKTeco</c>. Empty when not recorded.</summary>
        [MaxLength(255)]
        public string Manufacturer { get; set; } = string.Empty;

        /// <summary>Gets or sets the manufacturer's website, e.g. <c>https://www.zkteco.com</c>. Empty when not recorded.</summary>
        [MaxLength(255)]
        public string WebsiteUrl { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        #region not mapped
        [NotMapped]
        public override string Title => DisplayName;
        #endregion
    }
}
