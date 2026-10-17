using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A kind of punch device an administrator can give an <see cref="AttendanceDevice"/>, such as a
    /// fingerprint reader or a mobile app. A catalog of names only: nothing in the code depends on a
    /// particular type. Never deleted, only deactivated.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceDeviceType), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceDeviceType : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the type's name, e.g. <c>Biometric</c>. Unique per tenant, ignoring case.</summary>
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether a device can be given this type. A device that has it keeps it.</summary>
        public bool IsActive { get; set; } = true;

        #region not mapped
        [NotMapped]
        public override string Title => Name;
        #endregion
    }
}
