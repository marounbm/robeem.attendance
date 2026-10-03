using SmartEnum = robeem.System.Contract.SmartEnum;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace robeem.DataModel
{
    [Serializable, Table(nameof(TimeScheduleEmployee), Schema = nameof(SmartEnum.Module.HR))]
    public class TimeScheduleEmployee : SystemEntity, ISeedData
    {
        [Required]
        public int TimeScheduleId { get; set; }
        [Required]
        public int TeamId { get; set; }
        public TimeScheduleTeam? Team { get; set; }
        [Required]
        public int EmployeeId { get; set; }
        public Profile? Employee { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => PartitionName;
        #endregion
    }
}
