using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace robeem.DataModel
{
    [Serializable]
    public class TimeShiftEntity : Entity
    {
        [Required]
        [MaxLength(5)]
        [Column(Order = 101)]
        public string StartTime { get; set; } = "09:00";
        [Required]
        [MaxLength(5)]
        [Column(Order = 102)]
        public string EndTime { get; set; } = "17:00";

        [Column(Order = 103)]
        public bool Overtime { get; set; } = false;

        [Column(Order = 104)]
        public bool BreakTime { get; set; } = false;

        [Required]
        [MaxLength(5)]
        [Column(Order = 105)]
        public string BreakStartTime { get; set; } = "00:00";
        [Required]
        [MaxLength(5)]
        [Column(Order = 106)]
        public string BreakEndTime { get; set; } = "00:00";

        #region not mapped
        public DateTime DisplayStartTime => Set.asTime(StartTime);
        public DateTime DisplayEndTime => Set.asTime(EndTime);
        public DateTime DisplayBreakStartTime => Set.asTime(BreakStartTime);
        public DateTime DisplayBreakEndTime => Set.asTime(BreakEndTime);
        public double WorkHours => Set.TimeSpan(StartTime, EndTime);
        public double BreakHours => BreakTime ? Set.TimeSpan(BreakStartTime, BreakEndTime) : 0;
        public double TotalHours => BreakTime ? Set.TimeSpan(StartTime, EndTime, BreakStartTime, BreakEndTime) : WorkHours;
        #endregion
    }
}
