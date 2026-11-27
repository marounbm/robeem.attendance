using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A payroll administrator's decision about one line of an employee's compensation report: a day's overtime or under time. Additive
    /// (CLAUDE.md §1.7): a new decision for the same line supersedes the earlier one, and nothing is edited or deleted.
    /// </summary>
    [Serializable]
    [Table(nameof(PayCompensationDecision), Schema = nameof(SmartEnum.Module.Attendance))]
    public class PayCompensationDecision : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        /// <summary>Gets or sets the attendance date of the line (a date, without a time).</summary>
        public DateTime AttendanceDate { get; set; }

        public CompensationLineKind LineKind { get; set; }

        public CompensationDecisionKind Decision { get; set; }

        /// <summary>Gets or sets the minutes the line had when it was decided; a recalculation that changes them asks for a new decision.</summary>
        public int Minutes { get; set; }

        /// <summary>Gets or sets the ids of the employee leaves a leave decision created, comma separated; empty for any other decision.</summary>
        [MaxLength(100)]
        public string LeaveIds { get; set; } = string.Empty;

        /// <summary>Gets or sets who decided: the authenticated payroll administrator's employee id.</summary>
        [Required]
        public int DecidedById { get; set; }

        public Profile? DecidedBy { get; set; }

        public DateTime DecidedAt { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => $"{LineKind} {Decision} — {AttendanceDate:yyyy-MM-dd}";
        #endregion
    }
}
