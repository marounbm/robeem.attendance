using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// Attendance rules (rounding, grace periods, overtime, correction workflow, interpretation) for
    /// one <see cref="DataModel.TimeSchedule"/>, or for the whole tenant when
    /// <see cref="TimeScheduleId"/> is null (CLAUDE.md §3; study §13.2, §14.2). <c>Title</c> is the
    /// policy name an HR admin gives it.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendancePolicy), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendancePolicy : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the schedule this policy applies to; null for the tenant-wide default.</summary>
        public int? TimeScheduleId { get; set; }

        public TimeSchedule? TimeSchedule { get; set; }

        public int RoundingRuleMinutes { get; set; }

        public int LateGraceMinutes { get; set; }

        public int EarlyDepartureGraceMinutes { get; set; }

        public int OvertimeThresholdMinutes { get; set; }

        public bool RequiresOvertimePreApproval { get; set; }

        public CorrectionApprovalWorkflow CorrectionApprovalWorkflow { get; set; } = CorrectionApprovalWorkflow.Manager;

        public UnlabeledPunchInterpretationMode UnlabeledPunchInterpretationMode { get; set; } = UnlabeledPunchInterpretationMode.Off;

        /// <summary>Gets or sets the width of the time bucket used by the normalization duplicate key (CLAUDE.md §1.4).</summary>
        public int DuplicateTimeWindowSeconds { get; set; } = 120;

        /// <summary>Gets or sets how long after a shift ends a still-absent punch waits before it is raised as an exception (study §9.2).</summary>
        public int MissingPunchGraceMinutes { get; set; } = 120;

        /// <summary>
        /// Gets or sets the length below which a break is ignored: a toilet visit, not a break. Its two punches are left out,
        /// so the time counts as work and does not count towards the break rules. 0 turns this off.
        /// </summary>
        public int IgnoreBreaksShorterThanMinutes { get; set; } = 10;

        /// <summary>Gets or sets how many minutes earlier or later than scheduled a break may start before it is early or late.</summary>
        public int BreakStartGraceMinutes { get; set; } = 15;

        /// <summary>Gets or sets how many minutes the breaks may last beyond the scheduled break before they are long.</summary>
        public int LongBreakGraceMinutes { get; set; } = 5;

        /// <summary>Gets or sets a value indicating whether a long break raises an exception. When not, it is only recorded on the day.</summary>
        public bool RaiseExceptionOnLongBreak { get; set; }

        /// <summary>Gets or sets a value indicating whether a late break raises an exception. When not, it is only recorded on the day.</summary>
        public bool RaiseExceptionOnLateBreak { get; set; }

        /// <summary>Gets or sets a value indicating whether an early break raises an exception. When not, it is only recorded on the day.</summary>
        public bool RaiseExceptionOnEarlyBreak { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a shift with a scheduled break, worked with no break, raises an exception
        /// (the break punches are reported missing, so a person can supply them). When not, the day is complete without a break.
        /// </summary>
        public bool RaiseExceptionOnNoBreak { get; set; } = true;

        #region not mapped
        [NotMapped]
        public bool IsTenantDefault => TimeScheduleId is null;
        #endregion
    }
}
