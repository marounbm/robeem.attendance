using SmartEnum = robeem.System.Contract.SmartEnum;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace robeem.DataModel
{
    [Serializable, Table(nameof(TimeScheduleTeam), Schema = nameof(SmartEnum.Module.HR))]
    public class TimeScheduleTeam : Entity, ISeedData
    {
        [Required]
        public int TimeScheduleId { get; set; }
        public TimeSchedule? TimeSchedule { get; set; }

        public bool AllInclusive { get; set; } = true;
    }
}
