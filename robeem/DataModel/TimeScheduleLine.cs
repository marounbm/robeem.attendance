using SmartEnum = robeem.System.Contract.SmartEnum;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace robeem.DataModel
{
    [Serializable, Table(nameof(TimeScheduleLine), Schema = nameof(SmartEnum.Module.HR))]
    public class TimeScheduleLine : SystemEntity, ISeedData
    {
        [Required]
        public int TimeScheduleId { get; set; }
        public TimeSchedule? TimeSchedule { get; set; }

        [Required]
        public int TeamId { get; set; }
        public TimeScheduleTeam? Team { get; set; }
        [Required]
        public int ShiftId { get; set; }
        public TimeScheduleShift? Shift { get; set; }
        [Required]
        public int CycleDay { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => PartitionName;
        public string UniqueId => $"{TimeScheduleId}-{TeamId}-{CycleDay}-{ShiftId}";
        #endregion
    }
}
