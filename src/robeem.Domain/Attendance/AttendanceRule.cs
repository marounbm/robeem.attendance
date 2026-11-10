using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// One rule of an <see cref="AttendanceRuleSet"/>: when all its conditions hold, its action is taken. The name is the inherited
    /// <see cref="Entity.Title"/>. Rules are looked at in <see cref="Priority"/> order, lowest first; for a finding, the first rule whose
    /// conditions hold decides.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceRule), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceRule : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int AttendanceRuleSetId { get; set; }

        public AttendanceRuleSet? AttendanceRuleSet { get; set; }

        public int Priority { get; set; }

        public bool IsEnabled { get; set; } = true;

        public RuleStage Stage { get; set; }

        public RuleAction Action { get; set; }

        /// <summary>Gets or sets the finding a raise or suppress rule is about; null for an interpretation rule.</summary>
        public RuleFinding? Finding { get; set; }

        /// <summary>Gets or sets why a finding is suppressed: required for <see cref="RuleAction.SuppressException"/>, null otherwise.</summary>
        [MaxLength(500)]
        public string? Reason { get; set; }

        public ICollection<AttendanceRuleCondition> Conditions { get; set; } = new List<AttendanceRuleCondition>();
    }
}
