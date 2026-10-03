using SmartEnum = robeem.System.Contract.SmartEnum;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace robeem.DataModel
{
    [Serializable, Table(nameof(TimeSchedule), Schema = nameof(SmartEnum.Module.HR))]
    public class TimeSchedule : Entity, ISeedData
    {
        [Required]
        public int CyclePeriod { get; set; } = 7;
        [Required]
        public DayOfWeek FirstCycleDay { get; set; } = DayOfWeek.Monday;
        [Required]
        public bool Scheduling { get; set; } = false;
        [Required]
        public DateTime LastScheduleDate { get; set; } = DateTime.MinValue;
        [Required]
        public int LastScheduleDay { get; set; } = 1;

        public bool SystemSchedule { get; set; } = false;

        public string? Description { get; set; }

        #region not mapped
        public int CycleStartDay => LastScheduleDay == CyclePeriod ? 1 : LastScheduleDay + 1;
        #endregion

        #region method
        public int GetCycleDay(DateTime date)
        {
            var _lastScheduleDate = LastScheduleDate;

            if (LastScheduleDate == DateTime.MinValue)
            {
                int _difference = (int)FirstCycleDay - (int)date.DayOfWeek;
                _difference = _difference > 0 ? _difference - 7 : _difference;

                _lastScheduleDate = date.AddDays(_difference);
            }

            int _dayDifference = (int)(date - _lastScheduleDate).TotalDays;
            int _cycleDay = (LastScheduleDay + _dayDifference) % CyclePeriod;
            if (_cycleDay <= 0) _cycleDay += CyclePeriod;

            return _cycleDay;
        }
        #endregion
    }
}
