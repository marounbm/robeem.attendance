using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// The rules that decide how breaks are read and which findings become exceptions, for one attendance policy's scope: one time
    /// schedule, or the whole tenant when <see cref="TimeScheduleId"/> is null. A published set is never edited: a change is a new
    /// draft that is published as the next <see cref="Version"/>. With no published set, the rules are derived from the policy.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceRuleSet), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceRuleSet : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the schedule the rules are for; null for the tenant-wide set.</summary>
        public int? TimeScheduleId { get; set; }

        public TimeSchedule? TimeSchedule { get; set; }

        /// <summary>
        /// Gets or sets the number of this publication. It is taken from one sequence for the whole tenant, so a bigger number is always a later
        /// publication; 0 is "derived from the policy". A draft has none until it is published.
        /// </summary>
        public int Version { get; set; }

        public RuleSetStatus Status { get; set; } = RuleSetStatus.Draft;

        /// <summary>Gets or sets when the set was published, as local wall-clock time.</summary>
        public DateTime? PublishedAt { get; set; }

        /// <summary>Gets or sets who published it (an employee id); null when the system did, or while a draft.</summary>
        public int? PublishedById { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the thresholds that decide whether there is a finding at all (how far over a break may go, how
        /// far from its scheduled start, how long a no-show waits) are written in the rules as conditions. A set made before that has none, and
        /// the policy's own numbers still apply to its rules, so publishing it moved nobody's behaviour. Every set published since says true.
        /// </summary>
        public bool ThresholdsInRules { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the set has a say about a day worked short (the rule for <c>ShortHours</c>, on or off, or taken
        /// out on purpose). A set made before that finding existed has none, and is read with the policy's own rule for it added, on. Every set
        /// made since says true, so deleting the rule from one is a choice that stays.
        /// </summary>
        public bool ShortHoursInRules { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the set has a say about the automatic fixes of punches (the four rules that read a lone check-out or
        /// check-in, or keep the first of two close punches). A set made before they existed has none and is read with them added, all off, so it behaves
        /// as published. Every set made since says true.
        /// </summary>
        public bool AutoFixesInRules { get; set; }

        public ICollection<AttendanceRule> Rules { get; set; } = new List<AttendanceRule>();
    }
}
