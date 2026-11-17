using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// The attendance settings of a tenant: one row per tenant, made when the first setting is saved.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceSettings), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceSettings : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the date the punching started (a date, without a time): no attendance is expected before it. A punch before it
        /// is refused at normalization, a correction can't be made for an earlier day, and looking for missing days starts here.
        /// Null when the tenant has not set one.
        /// </summary>
        public DateTime? StartDate { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => "Attendance settings";
        #endregion
    }
}
