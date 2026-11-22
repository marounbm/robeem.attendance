using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// Marks a <see cref="TimeSchedule"/> as on demand: a schedule for employees paid by the hour. It has no shifts and no roster,
    /// so nobody on it is expected at a given time; what it gives is a home for the group (its own attendance policy and rules).
    /// A row of its own because the supplied <see cref="TimeSchedule"/> file is not edited. Present means on demand.
    /// </summary>
    [Serializable]
    [Table(nameof(OnDemandTimeSchedule), Schema = nameof(SmartEnum.Module.HR))]
    public class OnDemandTimeSchedule : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int TimeScheduleId { get; set; }

        public TimeSchedule? TimeSchedule { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => TimeSchedule?.Title ?? PartitionName;
        #endregion
    }
}
