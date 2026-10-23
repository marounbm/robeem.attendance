using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// Which employee a vendor's employee id stands for, per device: the same id can mean different people on
    /// different devices. Normalization resolves the punch's device first, then
    /// <see cref="RawAttendanceTransaction.ExternalEmployeeId"/> through that device's mappings.
    /// </summary>
    [Serializable]
    [Table(nameof(ExternalEmployeeIdMapping), Schema = nameof(SmartEnum.Module.Attendance))]
    public class ExternalEmployeeIdMapping : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int DeviceId { get; set; }

        public AttendanceDevice? Device { get; set; }

        /// <summary>Gets or sets the employee id exactly as the vendor sends it (compared case-sensitively). Unique per device.</summary>
        [Required]
        [MaxLength(255)]
        public string ExternalEmployeeId { get; set; } = string.Empty;

        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => ExternalEmployeeId;
        #endregion
    }
}
