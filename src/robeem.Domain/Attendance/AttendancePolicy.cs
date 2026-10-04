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

        #region not mapped
        [NotMapped]
        public bool IsTenantDefault => TimeScheduleId is null;
        #endregion
    }
}
