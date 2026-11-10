using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// The calculated attendance of one employee for one shift (study §8, §13.2). Never rewritten:
    /// a recalculation that changes anything writes a new <see cref="Version"/> and retires this
    /// one (<see cref="IsCurrent"/> false, <see cref="SupersededByRecordId"/> set).
    /// </summary>
    /// <remarks>
    /// Times are local wall-clock times, like the schedule. <see cref="AttendanceDate"/> is the
    /// date the shift starts on, also for an overnight shift (study §17.1).
    /// </remarks>
    [Serializable]
    [Table(nameof(AttendanceRecord), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceRecord : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        public DateTime AttendanceDate { get; set; }

        /// <summary>Gets or sets the scheduled shift; null for work outside any scheduled shift.</summary>
        public int? TimeScheduleShiftId { get; set; }

        public TimeScheduleShift? TimeScheduleShift { get; set; }

        public DateTime? ScheduledStart { get; set; }

        public DateTime? ScheduledEnd { get; set; }

        public DateTime? ActualCheckIn { get; set; }

        public AttendancePunchSource? ActualCheckInSource { get; set; }

        public DateTime? ActualCheckOut { get; set; }

        public AttendancePunchSource? ActualCheckOutSource { get; set; }

        public DateTime? ActualBreakOut { get; set; }

        public AttendancePunchSource? ActualBreakOutSource { get; set; }

        public DateTime? ActualBreakIn { get; set; }

        public AttendancePunchSource? ActualBreakInSource { get; set; }

        /// <summary>Gets or sets the time worked: the elapsed time of the work segments, breaks excluded.</summary>
        public TimeSpan WorkingHours { get; set; }

        public TimeSpan BreakDuration { get; set; }

        public int LateMinutes { get; set; }

        public int EarlyDepartureMinutes { get; set; }

        public int OvertimeMinutes { get; set; }

        /// <summary>
        /// Gets or sets the minutes of the shift covered by part-day approved leave when the day was calculated. Late and early minutes, a
        /// missing break and overtime are measured with them taken out. Kept so a later change to the leave shows as a different calculation.
        /// </summary>
        public int ExcusedMinutes { get; set; }

        /// <summary>
        /// Gets or sets the number of the published rule set the day was last calculated with; 0 when no set was published and the rules came from
        /// the policy. Not part of the calculation: it is brought up to date when the day is calculated again, and tells the look-back which days a
        /// newly published set has not reached.
        /// </summary>
        public int RuleSetVersion { get; set; }

        /// <summary>Gets or sets what was found about the break: long, late, early or none taken. Recorded whatever the policy raises as an exception.</summary>
        public BreakFindings BreakFindings { get; set; }

        public AttendanceRecordStatus Status { get; set; } = AttendanceRecordStatus.Calculated;

        /// <summary>Gets or sets the ids of the normalized transactions the calculation used: comma-separated, ascending.</summary>
        public string? SourceNormalizedTransactionIds { get; set; }

        /// <summary>Gets or sets the ids of the applied corrections the calculation used: comma-separated, ascending.</summary>
        public string? SourceCorrectionIds { get; set; }

        public int Version { get; set; } = 1;

        /// <summary>Gets or sets a value indicating whether this is the current version for its employee, date and shift.</summary>
        public bool IsCurrent { get; set; } = true;

        /// <summary>Gets or sets the version that replaced this one. Null while current, and when the shift is no longer scheduled.</summary>
        public int? SupersededByRecordId { get; set; }

        public AttendanceRecord? SupersededByRecord { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => $"Attendance {AttendanceDate:yyyy-MM-dd} v{Version}";
        #endregion
    }
}
