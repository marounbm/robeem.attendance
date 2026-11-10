using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>One condition of an <see cref="AttendanceRule"/>: a fact compared with a whole number. All of a rule's conditions must hold.</summary>
    [Serializable]
    [Table(nameof(AttendanceRuleCondition), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceRuleCondition : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int AttendanceRuleId { get; set; }

        public AttendanceRule? AttendanceRule { get; set; }

        public RuleFact Fact { get; set; }

        public RuleOperator Operator { get; set; }

        /// <summary>Gets or sets the number the fact is compared with; 1 or 0 for a yes-or-no fact.</summary>
        public int Value { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => $"{Fact} {Operator} {Value}";
        #endregion
    }
}
