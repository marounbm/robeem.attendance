using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// One employee's day: the sum of that date's current <see cref="AttendanceRecord"/>s (study
    /// §13.2, §17). A derived rollup, recomputed in place whenever the day is processed.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceDaySummary), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceDaySummary : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        public DateTime AttendanceDate { get; set; }

        public TimeSpan TotalWorkingHours { get; set; }

        public TimeSpan TotalOvertimeHours { get; set; }

        public TimeSpan TotalBreakHours { get; set; }

        /// <summary>Gets or sets the number of current records that day, unscheduled work included.</summary>
        public int ShiftCount { get; set; }

        /// <summary>Gets or sets the number of attendance exceptions that day that are not closed yet.</summary>
        public int ExceptionCount { get; set; }

        public AttendanceRecordStatus Status { get; set; } = AttendanceRecordStatus.Calculated;

        #region not mapped
        [NotMapped]
        public override string Title => $"Attendance day {AttendanceDate:yyyy-MM-dd}";
        #endregion
    }
}
